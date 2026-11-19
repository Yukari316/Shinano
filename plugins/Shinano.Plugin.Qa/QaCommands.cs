using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Qa.Persistence;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.MessageWaiting;
using Sora.Entities.Segments;
using System.Text.RegularExpressions;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.Qa;

/// <summary>
/// 群内的录入、删除与列表指令。录入与删除限群主、管理员与 Super User（拒绝时不回复），列表所有成员可用。
/// 录入到已有问题时进入覆盖确认：只接受发起人在同群回复的 y 或 n，其他消息照常处理；只在覆盖成功或失败时回复。
/// </summary>
[CommandGroup(Name = "qa")]
public sealed partial class QaCommands
{
    private const string DELETE_PREFIX       = "不要回答";
    private const string DELETE_REGEX_PREFIX = "RG不要回答";
    private const string BASE64_PREFIX       = "base64://";
    private const string YES                 = "y";
    private const string NO                  = "n";
    private const int    FIRST_PAGE          = 1;

    private const string RECORDED       = "我记住了！";
    private const string DELETED        = "我不再回答这个问题了！";
    private const string NOT_FOUND      = "没有这样的问题";
    private const string EMPTY_LIST     = "别急，还没有任何问题";
    private const string UNSUPPORTED    = "叽里咕噜说啥呢";
    private const string QUESTION_TEXT  = "问题只能是文字";
    private const string QUOTE_MISSING  = "无法获取引用的消息";
    private const string MEDIA_FAILED   = "录入失败：媒体保存失败";
    private const string CHANGED        = "这个问题被删了！";
    private const string RETRY          = "记录失败";

    private readonly QaStore                      _store;
    private readonly QaMediaStore                 _media;
    private readonly QaAnswerSender               _sender;
    private readonly IPluginInitializationContext _context;
    private readonly ILogger<QaCommands>          _logger;

    /// <summary>创建指令组。</summary>
    public QaCommands(
        QaStore                      store,
        QaMediaStore                 media,
        QaAnswerSender               sender,
        IPluginInitializationContext context,
        ILogger<QaCommands>          logger)
    {
        _store   = store;
        _media   = media;
        _sender  = sender;
        _context = context;
        _logger  = logger;
    }

    private QaConfig Config => _context.Config<QaConfig>().Value;

    /// <summary>有人问…你答…：录入全字匹配词条。</summary>
    [Command(Expressions = ["^有人问[\\s\\S]*你答"], MatchType = MatchType.Regex, SourceScope = MessageSourceScope.Group, Description = "录入有人问")]
    [RequirePermission(MemberRole.Admin)]
    public ValueTask RecordInlineAsync(MessageReceivedEvent e) =>
        RecordAsync(e, QaMatchType.Full, QaRecordingParser.ParseInline(e.Message.Body, QaRecordingParser.INLINE_PREFIX));

    /// <summary>RG有人问…你答…：录入正则词条。</summary>
    [Command(Expressions = ["^RG有人问[\\s\\S]*你答"], MatchType = MatchType.Regex, SourceScope = MessageSourceScope.Group, Description = "录入正则有人问")]
    [RequirePermission(MemberRole.Admin)]
    public ValueTask RecordInlineRegexAsync(MessageReceivedEvent e) =>
        RecordAsync(e, QaMatchType.Regex, QaRecordingParser.ParseInline(e.Message.Body, QaRecordingParser.INLINE_REGEX_PREFIX));

    /// <summary>引用一条消息并发送"记录有人问…"：以被引用的整条消息为回答，录入全字匹配词条。</summary>
    [Command(Expressions = ["^记录有人问[\\s\\S]+"], MatchType = MatchType.Regex, SourceScope = MessageSourceScope.Group, Description = "引用录入有人问")]
    [RequirePermission(MemberRole.Admin)]
    public ValueTask RecordQuotedAsync(MessageReceivedEvent e) =>
        RecordQuotedAsync(e, QaMatchType.Full, QaRecordingParser.QUOTE_PREFIX);

    /// <summary>引用一条消息并发送"记录RG有人问…"：录入正则词条。</summary>
    [Command(Expressions = ["^记录RG有人问[\\s\\S]+"], MatchType = MatchType.Regex, SourceScope = MessageSourceScope.Group, Description = "引用录入正则有人问")]
    [RequirePermission(MemberRole.Admin)]
    public ValueTask RecordQuotedRegexAsync(MessageReceivedEvent e) =>
        RecordQuotedAsync(e, QaMatchType.Regex, QaRecordingParser.QUOTE_REGEX_PREFIX);

    /// <summary>不要回答…：删除全字匹配词条。</summary>
    [Command(Expressions = ["^不要回答[\\s\\S]+"], MatchType = MatchType.Regex, SourceScope = MessageSourceScope.Group, Description = "删除有人问")]
    [RequirePermission(MemberRole.Admin)]
    public ValueTask DeleteAsync(MessageReceivedEvent e) =>
        DeleteAsync(e, QaMatchType.Full, DELETE_PREFIX);

    /// <summary>RG不要回答…：删除正则词条。</summary>
    [Command(Expressions = ["^RG不要回答[\\s\\S]+"], MatchType = MatchType.Regex, SourceScope = MessageSourceScope.Group, Description = "删除正则有人问")]
    [RequirePermission(MemberRole.Admin)]
    public ValueTask DeleteRegexAsync(MessageReceivedEvent e) =>
        DeleteAsync(e, QaMatchType.Regex, DELETE_REGEX_PREFIX);

    /// <summary>看看有人问[页码]：以列表卡片列出本群词条。</summary>
    [Command(Expressions = ["^看看有人问\\s*\\d*$"], MatchType = MatchType.Regex, SourceScope = MessageSourceScope.Group, Description = "列出有人问")]
    public async ValueTask ListAsync(MessageReceivedEvent e)
    {
        IReadOnlyList<QaEntry> entries = await _store.GroupAsync(e.Message.GroupId);
        if (entries.Count == 0)
        {
            await e.ReplyAsync(new MessageBody(EMPTY_LIST));
            return;
        }

        string digits = PageNumber().Match(e.Message.Body.GetText()).Value;
        int number = int.TryParse(digits, out int parsed) ? parsed : FIRST_PAGE;
        if (QaListPage.Take(entries, number, Config.PageSize) is not { } page)
        {
            await e.ReplyAsync(new MessageBody($"你看看哪还有第{number}页，只有{(entries.Count + Config.PageSize - 1) / Config.PageSize}页！"));
            return;
        }

        MessageBody reply;
        try
        {
            byte[] png = _context.Images.RenderList(page.ToCard());
            reply = new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "绘制有人问列表失败，改发文字");
            reply = new MessageBody(page.ToText());
        }

        await e.ReplyAsync(reply);
    }

    private async ValueTask RecordQuotedAsync(MessageReceivedEvent e, QaMatchType matchType, string prefix)
    {
        QaParseResult command = QaRecordingParser.ParseQuoteCommand(e.Message.Body, prefix);
        if (command.Error != QaParseError.None || command.Reply is not { } reply)
        {
            await ReplyParseErrorAsync(e, command.Error);
            return;
        }

        MessageBody? quoted = reply.Content;
        if (quoted is null)
        {
            ApiResult<MessageContext> original = await e.Api.GetMessageAsync(MessageSourceType.Group, e.Message.GroupId, reply.TargetId);
            quoted = original is { IsSuccess: true, Data: { } message } ? message.Body : null;
            if (quoted is null)
                _logger.LogWarning("无法获取引用的消息（群 {GroupId}，消息 {MessageId}）：{Code} {Message}", e.Message.GroupId, reply.TargetId, original.Code, original.Message);
        }

        if (quoted is null)
        {
            await e.ReplyAsync(new MessageBody(QUOTE_MISSING));
            return;
        }

        await RecordAsync(e, matchType, QaRecordingParser.ParseQuotedAnswer(command.Question, quoted));
    }

    private async ValueTask RecordAsync(MessageReceivedEvent e, QaMatchType matchType, QaParseResult parsed)
    {
        if (parsed.Error != QaParseError.None)
        {
            await ReplyParseErrorAsync(e, parsed.Error);
            return;
        }

        if (await _store.RejectQuestionAsync(parsed.Question, matchType) is { } rejection)
        {
            await e.ReplyAsync(new MessageBody(rejection));
            return;
        }

        long groupId = e.Message.GroupId;
        if (await _store.FindAsync(groupId, matchType, parsed.Question) is { } existing)
        {
            await ConfirmOverwriteAsync(e, existing, parsed);
            return;
        }

        string mediaKey = QaStore.NewMediaKey();
        if (await _media.StoreAsync(e.Api, groupId, parsed.Answer, mediaKey, CancellationToken.None) is not { } answer)
        {
            await e.ReplyAsync(new MessageBody(MEDIA_FAILED));
            return;
        }

        if (await _store.AddAsync(groupId, matchType, parsed.Question, answer, mediaKey, e.Message.SenderId) is null)
        {
            await _media.RemoveAsync(mediaKey);
            await e.ReplyAsync(new MessageBody(RETRY));
            return;
        }

        _logger.LogInformation("录入有人问：群 {GroupId}，{MatchType}，录入人 {UserId}", groupId, matchType, e.Message.SenderId);
        await e.ReplyAsync(new MessageBody(RECORDED));
    }

    // 回复 n 或超时都保留原词条且不回复，只在覆盖成功或失败时回复。等待期间同一发起人再次录入同一指令时，
    // 由 Sora 的防重入直接占用该消息，不另行提示。
    private async ValueTask ConfirmOverwriteAsync(MessageReceivedEvent e, QaEntry existing, QaParseResult parsed)
    {
        MessageBody prompt = new($"已经有这个问题了，回复 {YES} 覆盖，回复 {NO} 保留原回答\n问题：{existing.Question}\n原回答：");
        foreach (Segment segment in await _sender.PreviewAsync(existing.Answer)) prompt.Add(segment);
        prompt.Add(SegmentBuilder.Text("\n新回答："));
        foreach (Segment segment in QaAnswerSender.PreviewDraft(parsed.Answer)) prompt.Add(segment);
        await e.ReplyAsync(prompt);

        MessageReceivedEvent? reply = await e.WaitForNextMessageAsync(IsYesOrNo, TimeSpan.FromSeconds(Config.ConfirmTimeoutSeconds));
        if (reply is null || !string.Equals(reply.Message.Body.GetText().Trim(), YES, StringComparison.OrdinalIgnoreCase)) return;

        await OverwriteAsync(e, existing, parsed);
    }

    private async Task OverwriteAsync(MessageReceivedEvent e, QaEntry existing, QaParseResult parsed)
    {
        string mediaKey = QaStore.NewMediaKey();
        if (await _media.StoreAsync(e.Api, existing.GroupId, parsed.Answer, mediaKey, CancellationToken.None) is not { } answer)
        {
            await e.ReplyAsync(new MessageBody(MEDIA_FAILED));
            return;
        }

        if (!await _store.ReplaceAsync(existing, answer, mediaKey, e.Message.SenderId))
        {
            await _media.RemoveAsync(mediaKey);
            await e.ReplyAsync(new MessageBody(CHANGED));
            return;
        }

        await _media.RemoveAsync(existing.MediaKey);
        _logger.LogInformation("覆盖有人问：群 {GroupId}，词条 {EntryId}，录入人 {UserId}", existing.GroupId, existing.Id, e.Message.SenderId);
        await e.ReplyAsync(new MessageBody(RECORDED));
    }

    private async ValueTask DeleteAsync(MessageReceivedEvent e, QaMatchType matchType, string prefix)
    {
        string question = e.Message.Body.GetText().Trim()[prefix.Length..].Trim();
        if (question.Length == 0 || await _store.DeleteAsync(e.Message.GroupId, matchType, question) is not { } deleted)
        {
            await e.ReplyAsync(new MessageBody(NOT_FOUND));
            return;
        }

        await _media.RemoveAsync(deleted.MediaKey);
        _logger.LogInformation("删除有人问：群 {GroupId}，词条 {EntryId}，操作人 {UserId}", deleted.GroupId, deleted.Id, e.Message.SenderId);
        await e.ReplyAsync(new MessageBody(DELETED));
    }

    private static ValueTask<SendMessageResult> ReplyParseErrorAsync(MessageReceivedEvent e, QaParseError error) =>
        e.ReplyAsync(new MessageBody(error == QaParseError.QuestionNotText ? QUESTION_TEXT : UNSUPPORTED));

    // 只接受纯文本的 y 或 n（不区分大小写）；连续对话只会把同一发起人在同一群的消息交给这里。
    private static bool IsYesOrNo(MessageReceivedEvent message)
    {
        MessageBody body = message.Message.Body;
        if (body.Count == 0 || body.Any(segment => segment is not TextSegment)) return false;
        string text = body.GetText().Trim();
        return text.Equals(YES, StringComparison.OrdinalIgnoreCase) || text.Equals(NO, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\\d+")]
    private static partial Regex PageNumber();
}
