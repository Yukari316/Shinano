using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Plugin.Gemini.Api;
using Shinano.Plugin.Gemini.Persistence;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>
/// Gemini 普通事件消息处理器：按响应白名单过滤群聊与好友私聊（忽略临时会话），匹配可配置前缀，
/// 处理新对话清空命令、多模态输入读取、按发言者全局冷却、内存多轮上下文管理与终末地长文卡片回复。
/// 回复聊天的只有固定文案，失败原因只写日志。
/// </summary>
public sealed class GeminiMessageHandler
{
    private const string BASE64_PREFIX = "base64://";

    private static readonly string[] ResetCommands = ["新对话", "重置", "reset", "clear"];

    private readonly GeminiScopeStore              _scope;
    private readonly GeminiModelSettingsStore      _modelSettings;
    private readonly GeminiUsageStore              _usage;
    private readonly GeminiApiClient               _api;
    private readonly GeminiCooldownTracker         _cooldown;
    private readonly GeminiInputReader             _inputReader;
    private readonly GeminiConversationStore       _conversations;
    private readonly IPluginConfig<GeminiConfig>   _config;
    private readonly IPluginSecrets<GeminiSecrets> _secrets;
    private readonly IPluginImages                 _images;
    private readonly TimeProvider                  _time;
    private readonly ILogger<GeminiMessageHandler> _logger;

    /// <summary>创建消息处理器。</summary>
    public GeminiMessageHandler(
        GeminiScopeStore              scope,
        GeminiModelSettingsStore      modelSettings,
        GeminiUsageStore              usage,
        GeminiApiClient               api,
        GeminiCooldownTracker         cooldown,
        GeminiInputReader             inputReader,
        GeminiConversationStore       conversations,
        IPluginConfig<GeminiConfig>   config,
        IPluginSecrets<GeminiSecrets> secrets,
        IPluginImages                 images,
        TimeProvider                  time,
        ILogger<GeminiMessageHandler> logger)
    {
        _scope         = scope;
        _modelSettings = modelSettings;
        _usage         = usage;
        _api           = api;
        _cooldown      = cooldown;
        _inputReader   = inputReader;
        _conversations = conversations;
        _config        = config;
        _secrets       = secrets;
        _images        = images;
        _time          = time;
        _logger        = logger;
    }

    /// <summary>处理一条收到的消息事件。</summary>
    public async ValueTask HandleAsync(MessageReceivedEvent e)
    {
        // 仅支持群聊与好友私聊，临时会话一律忽略。
        if (e.Message.SourceType is not (MessageSourceType.Group or MessageSourceType.Friend))
            return;

        bool isGroup = e.Message.SourceType == MessageSourceType.Group;
        GeminiScopeKind scopeKind = isGroup ? GeminiScopeKind.Group : GeminiScopeKind.Private;
        long scopeId = isGroup ? (long)e.Message.GroupId : (long)e.Message.SenderId;
        if (!_scope.Allows(scopeKind, scopeId))
            return;

        GeminiConfig config = _config.Value;
        string prefix = config.TriggerPrefix.Trim();
        if (prefix.Length == 0) return;

        string rawText = e.Message.Body.GetText().Trim();
        if (!TryMatchPrefix(rawText, prefix, out string afterPrefix))
            return;

        GroupId groupId = isGroup ? e.Message.GroupId : default;
        UserId userId = e.Message.SenderId;
        string session = FormatSession(isGroup, groupId, userId);

        // 新对话清空命令：受白名单约束、不消耗冷却、终止事件链。
        if (IsResetCommand(afterPrefix))
        {
            e.IsContinueEventChain = false;
            _conversations.Clear(groupId, userId);
            _logger.LogInformation("Gemini 清空对话上下文：{Session}，发言者 {UserId}", session, (long)userId);
            await e.ReplyAsync(new MessageBody(ReplyTexts.NEW_CONVERSATION));
            return;
        }

        // 纯前缀且不带图片、未引用消息时直接静默忽略，不占事件链也不消耗冷却。
        if (afterPrefix.Length == 0 && !HasImageOrReply(e.Message.Body))
            return;

        string apiKey = _secrets.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            e.IsContinueEventChain = false;
            _logger.LogWarning("Gemini 触发失败（未配置 API Key）：{Session}，发言者 {UserId}", session, (long)userId);
            await e.ReplyAsync(new MessageBody(ReplyTexts.API_KEY_MISSING));
            return;
        }

        // 先占用会话与冷却再读取输入，生成中或冷却中的触发不必下载图片。
        int maxTurns = isGroup ? config.GroupMaxTurns : config.PrivateMaxTurns;
        TimeSpan idleTimeout = TimeSpan.FromMinutes(config.ConversationTimeoutMinutes);
        if (!_conversations.TryBeginTurn(groupId, userId, maxTurns, idleTimeout, out GeminiTurnLease lease))
        {
            e.IsContinueEventChain = false;
            _logger.LogInformation("Gemini 跳过触发（上一条仍在生成中）：{Session}，发言者 {UserId}", session, (long)userId);
            await e.ReplyAsync(new MessageBody(ReplyTexts.BUSY));
            return;
        }

        TimeSpan cooldown = TimeSpan.FromSeconds(Math.Max(0, config.UserCooldownSeconds));
        CooldownResult cooldownResult = _cooldown.TryAcquire(userId, cooldown, out CooldownTicket ticket, out int remainingSeconds);
        if (cooldownResult != CooldownResult.Acquired)
        {
            _conversations.AbortTurn(lease);
            e.IsContinueEventChain = false;
            if (cooldownResult == CooldownResult.Busy)
            {
                _logger.LogInformation("Gemini 跳过触发（该发言者在其他会话的请求仍在生成中）：{Session}，发言者 {UserId}", session, (long)userId);
                await e.ReplyAsync(new MessageBody(ReplyTexts.BUSY));
                return;
            }

            _logger.LogInformation(
                "Gemini 跳过触发（冷却中，剩余 {RemainingSeconds} 秒）：{Session}，发言者 {UserId}",
                remainingSeconds,
                session,
                (long)userId);
            await e.ReplyAsync(new MessageBody(ReplyTexts.Cooldown(remainingSeconds)));
            return;
        }

        GeminiInputReadResult input;
        try
        {
            input = await _inputReader.ReadAsync(e, afterPrefix, CancellationToken.None);
        }
        catch
        {
            // 异常由宿主记录；先释放占用，否则该发言者会一直被视为生成中。
            ReleaseTurn(lease, ticket);
            throw;
        }

        if (input.Status == GeminiInputReadStatus.Empty)
        {
            ReleaseTurn(lease, ticket);
            return;
        }

        e.IsContinueEventChain = false;
        if (input.Status == GeminiInputReadStatus.Failed)
        {
            ReleaseTurn(lease, ticket);
            _logger.LogWarning("Gemini 输入校验失败：{Session}，发言者 {UserId}，原因 {Reason}", session, (long)userId, input.ErrorMessage);
            await e.ReplyAsync(new MessageBody(input.ErrorMessage));
            return;
        }

        GeminiModelSettingsRecord settings = _modelSettings.Current;
        string model = GeminiModelCapabilities.NormalizeModelId(settings.Model);
        if (model.Length == 0) model = GeminiModelCapabilities.DEFAULT_MODEL;
        // 空 effort 表示保存模型时已确认它不支持思考，不能再按模型名推断出档位。
        string effort = settings.Effort.Length == 0 ? string.Empty : GeminiModelCapabilities.NormalizeEffort(model, settings.Effort);

        GeminiConversationMessage userMessage = BuildUserMessage(input);
        long inputMediaBytes = input.Images.Sum(image => (long)image.Data.Length);
        List<GeminiConversationMessage> history = GeminiConversationStore.TrimMedia(lease.History, GeminiApiClient.MAX_INLINE_MEDIA_BYTES - inputMediaBytes);

        _logger.LogInformation(
            "Gemini 触发对话：{Session}，发言者 {UserId}，模型 {Model}（effort={Effort}），轮数 [{Turn}/{Limit}]{ResetFlag}，文本 {TextLength} 字，图片 {ImageCount} 张",
            session,
            (long)userId,
            model,
            effort.Length > 0 ? effort : "无",
            lease.TurnNumber,
            lease.TurnLimit,
            lease.ResetPrevious ? "（新对话）" : string.Empty,
            input.Prompt.Length,
            input.Images.Count);

        GeminiGenerateRequest request = new()
        {
            BaseUrl            = config.BaseUrl,
            ProxyUrl           = config.ProxyUrl,
            ApiKey             = apiKey,
            Model              = model,
            Effort             = effort,
            SystemPrompt       = settings.SystemPrompt ?? string.Empty,
            IncludeThoughts    = config.IncludeThoughts,
            EnableGoogleSearch = config.EnableGoogleSearch,
            MaxSearchSources   = config.MaxSearchSources,
            TimeoutSeconds     = config.TimeoutSeconds,
            Messages           = [.. history, userMessage]
        };

        GeminiGenerateResponse response;
        try
        {
            response = await _api.GenerateAsync(request, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Gemini 请求超时：{Session}，发言者 {UserId}，模型 {Model}", session, (long)userId, model);
            await FailRequestAsync(e, lease, ticket, model, ReplyTexts.REQUEST_TIMEOUT);
            return;
        }
        catch (GeminiApiException exception)
        {
            _logger.LogWarning("Gemini 请求失败：{Session}，发言者 {UserId}，模型 {Model}，原因 {Reason}", session, (long)userId, model, exception.Message);
            await FailRequestAsync(e, lease, ticket, model, ReplyTexts.REQUEST_FAILED);
            return;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "处理 Gemini 对话请求时出现未预期异常：{Session}，发言者 {UserId}", session, (long)userId);
            await FailRequestAsync(e, lease, ticket, model, ReplyTexts.REQUEST_FAILED);
            return;
        }

        decimal costUsd = GeminiPricing.EstimateCostUsd(model, response.Usage, DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime));
        if (string.IsNullOrWhiteSpace(response.AnswerText))
        {
            ReleaseTurn(lease, ticket);
            await _usage.RecordAsync(model, succeeded: false, response.Usage, costUsd);
            _logger.LogWarning("Gemini 返回空内容：{Session}，发言者 {UserId}，模型 {Model}", session, (long)userId, model);
            await e.ReplyAsync(new MessageBody(ReplyTexts.EMPTY_RESPONSE));
            return;
        }

        await _usage.RecordAsync(model, succeeded: true, response.Usage, costUsd);
        IReadOnlyList<GeminiConversationPart> modelParts = response.ModelParts.Count > 0
            ? response.ModelParts
            : [new GeminiConversationPart { Text = response.AnswerText }];
        GeminiConversationMessage modelMessage = new()
        {
            Role  = "model",
            Parts = modelParts
        };
        _conversations.CommitTurn(lease, userMessage, modelMessage);

        string markdown = GeminiReplyFormatter.BuildMarkdown(response, config.IncludeThoughts, config.EnableGoogleSearch);
        string footerLeft = GeminiReplyFormatter.BuildFooterLeft(model, effort);
        string footerRight = GeminiReplyFormatter.BuildFooterRight(lease.TurnNumber, lease.TurnLimit);
        try
        {
            await ReplyCardOrFallbackAsync(e, markdown, footerLeft, footerRight);
        }
        finally
        {
            // 冷却从回复发出后开始计时。
            _cooldown.Complete(ticket);
        }

        _logger.LogInformation(
            "Gemini 回复完成：{Session}，发言者 {UserId}，轮数 [{Turn}/{Limit}]，回复 {AnswerLength} 字，耗时 {ElapsedMs} ms，{Usage}",
            session,
            (long)userId,
            lease.TurnNumber,
            lease.TurnLimit,
            response.AnswerText.Length,
            (long)response.Elapsed.TotalMilliseconds,
            response.Usage?.FormatSummary(response.Elapsed, costUsd) ?? "未返回 token 用量");
    }

    private static string FormatSession(bool isGroup, GroupId groupId, UserId userId) =>
        isGroup ? $"群 {(long)groupId}" : $"好友私聊 {(long)userId}";

    /// <summary>判断是否匹配前缀，并提取前缀后去掉首尾空白的内容。</summary>
    internal static bool TryMatchPrefix(string trimmedText, string prefix, out string afterPrefix)
    {
        if (!trimmedText.StartsWith(prefix, StringComparison.Ordinal))
        {
            afterPrefix = string.Empty;
            return false;
        }

        afterPrefix = trimmedText[prefix.Length..].Trim();
        return true;
    }

    /// <summary>判断前缀后文本是否为清空上下文指令。</summary>
    internal static bool IsResetCommand(string afterPrefix) =>
        ResetCommands.Any(command => string.Equals(afterPrefix, command, StringComparison.OrdinalIgnoreCase));

    private static bool HasImageOrReply(MessageBody body) =>
        body.OfType<ImageSegment>().Any() || body.OfType<ReplySegment>().Any();

    private static GeminiConversationMessage BuildUserMessage(GeminiInputReadResult input)
    {
        List<GeminiConversationPart> parts = [];
        if (input.Prompt.Length > 0)
            parts.Add(new GeminiConversationPart { Text = input.Prompt });
        foreach (GeminiMediaPart media in input.Images)
            parts.Add(new GeminiConversationPart { Media = media });

        return new GeminiConversationMessage
        {
            Role  = "user",
            Parts = parts
        };
    }

    // 本轮未产生回复：释放会话与冷却占用，不计入轮数也不开始冷却。
    private void ReleaseTurn(GeminiTurnLease lease, CooldownTicket ticket)
    {
        _conversations.AbortTurn(lease);
        _cooldown.Release(ticket);
    }

    private async Task FailRequestAsync(MessageReceivedEvent e, GeminiTurnLease lease, CooldownTicket ticket, string model, string reply)
    {
        ReleaseTurn(lease, ticket);
        await _usage.RecordAsync(model, succeeded: false, usage: null, costUsd: 0m);
        await e.ReplyAsync(new MessageBody(reply));
    }

    private async Task ReplyCardOrFallbackAsync(
        MessageReceivedEvent e,
        string               markdown,
        string               footerLeft,
        string               footerRight)
    {
        byte[]? cardPng = null;
        try
        {
            RenderedDocument rendered = _images.RenderDocument(
                new DocumentCard
                {
                    Markdown    = markdown,
                    Note        = footerLeft,
                    FooterRight = footerRight
                });
            cardPng = rendered.Image;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "渲染 Gemini 长文卡片失败，降级发送纯文本");
        }

        if (cardPng is not null)
        {
            SendMessageResult sent = await e.ReplyAsync(new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(cardPng)));
            if (sent.IsSuccess) return;
            _logger.LogWarning("发送 Gemini 长文卡片失败（{Code} {Error}），降级发送纯文本", sent.Code, sent.ErrorMessage);
        }

        string fallback = GeminiReplyFormatter.BuildPlainTextFallback(markdown, footerLeft, footerRight);
        await e.ReplyAsync(new MessageBody(fallback));
    }
}
