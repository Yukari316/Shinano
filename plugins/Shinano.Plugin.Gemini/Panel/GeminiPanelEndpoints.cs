using Microsoft.AspNetCore.Http;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Gemini.Api;
using Shinano.Plugin.Gemini.Persistence;
using Sora.Entities.Info;

namespace Shinano.Plugin.Gemini.Panel;

/// <summary>Gemini 专有面板的管理 API 处理方法，路由见 <see cref="GeminiPanelApi" />。</summary>
internal static class GeminiPanelEndpoints
{
    /// <summary>群聊与好友私聊的响应白名单。</summary>
    internal static IResult Scopes(GeminiScopeStore store) =>
        Results.Ok(
            new GeminiScopesView
            {
                Group   = new GeminiScopeView { Ids = store.Get(GeminiScopeKind.Group).Ids },
                Private = new GeminiScopeView { Ids = store.Get(GeminiScopeKind.Private).Ids }
            });

    /// <summary>保存一类会话的白名单，保存后立即生效。</summary>
    internal static async Task<IResult> SaveScopeAsync(GeminiScopeStore store, string kind, GeminiScopeUpdate update)
    {
        if (ParseKind(kind) is not { } scopeKind)
            return Results.NotFound(new { error = $"没有 {kind} 类会话" });
        if (update.Ids is null || update.Ids.Any(id => id <= 0))
            return Results.BadRequest(new { error = "号码必须是正整数" });

        await store.SaveAsync(
            new GeminiScopeRecord
            {
                Kind = scopeKind,
                Ids  = [.. update.Ids.Distinct().Order()]
            });
        return Results.NoContent();
    }

    /// <summary>可选的群或好友列表，附带已在名单中但 Bot 已不在的群或已不是好友的号码。</summary>
    internal static async Task<IResult> CandidatesAsync(
        IBotAccessor      bot,
        GeminiScopeStore  store,
        string            kind,
        CancellationToken cancellationToken)
    {
        if (ParseKind(kind) is not { } scopeKind)
            return Results.NotFound(new { error = $"没有 {kind} 类会话" });

        IReadOnlyList<GeminiScopeCandidateView> current = scopeKind == GeminiScopeKind.Group
            ? await GroupsAsync(bot, cancellationToken)
            : await FriendsAsync(bot, cancellationToken);
        HashSet<long> currentIds = [.. current.Select(candidate => candidate.Id)];
        IEnumerable<GeminiScopeCandidateView> missing = store.Get(scopeKind)
                                                             .Ids.Where(id => !currentIds.Contains(id))
                                                             .Select(id => new GeminiScopeCandidateView { Id = id });
        return Results.Ok(current.Concat(missing).ToArray());
    }

    /// <summary>读取当前模型与思考深度配置。</summary>
    internal static IResult GetModelSettings(GeminiModelSettingsStore store, IPluginSecrets<GeminiSecrets> secrets) =>
        Results.Ok(BuildModelSettingsView(store.Current, secrets, thinking: null));

    /// <summary>保存模型与思考深度配置，保存后立即生效。</summary>
    internal static async Task<IResult> SaveModelSettingsAsync(
        GeminiModelSettingsStore      store,
        IPluginSecrets<GeminiSecrets> secrets,
        GeminiModelSettingsUpdate     update)
    {
        string model = GeminiModelCapabilities.NormalizeModelId(update.Model);
        if (model.Length == 0)
            return Results.BadRequest(new { error = "模型名称不能为空" });
        if (model.Length > 128)
            return Results.BadRequest(new { error = "模型名称不能超过 128 个字符" });

        string effort = GeminiModelCapabilities.NormalizeEffort(model, update.Effort, update.Thinking);
        await store.SaveAsync(model, effort);

        return Results.Ok(BuildModelSettingsView(store.Current, secrets, update.Thinking));
    }

    /// <summary>保存多行系统提示词，保存后立即生效。</summary>
    internal static async Task<IResult> SaveSystemPromptAsync(
        GeminiModelSettingsStore      store,
        IPluginSecrets<GeminiSecrets> secrets,
        GeminiSystemPromptUpdate      update)
    {
        string normalized = (update.SystemPrompt ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        if (normalized.Length > 8000)
            return Results.BadRequest(new { error = "系统提示词不能超过 8000 字" });

        await store.SaveSystemPromptAsync(normalized);
        return Results.Ok(BuildModelSettingsView(store.Current, secrets, thinking: null));
    }

    private static GeminiModelSettingsView BuildModelSettingsView(
        GeminiModelSettingsRecord     current,
        IPluginSecrets<GeminiSecrets> secrets,
        bool?                         thinking)
    {
        string model = GeminiModelCapabilities.NormalizeModelId(current.Model);
        if (model.Length == 0) model = GeminiModelCapabilities.DEFAULT_MODEL;
        // 空 effort 表示保存时已确认该模型不支持思考；重新读取时没有模型列表的 thinking 标记，不能再按模型名推断。
        bool thinkingDisabled = current.Effort.Length == 0;
        IReadOnlyList<string> allowed = thinkingDisabled ? [] : GeminiModelCapabilities.GetAllowedEfforts(model, thinking);
        string effort = thinkingDisabled ? string.Empty : GeminiModelCapabilities.NormalizeEffort(model, current.Effort, thinking);

        return new GeminiModelSettingsView
        {
            Model          = model,
            Effort         = effort,
            AllowedEfforts = allowed,
            HasApiKey      = !string.IsNullOrWhiteSpace(secrets.Value.ApiKey),
            SystemPrompt   = current.SystemPrompt ?? string.Empty
        };
    }

    /// <summary>经配置中的 Base URL 与代理调用 Gemini API 拉取可用模型列表。</summary>
    internal static async Task<IResult> ListModelsAsync(
        GeminiApiClient               api,
        IPluginConfig<GeminiConfig>   config,
        IPluginSecrets<GeminiSecrets> secrets,
        CancellationToken             cancellationToken)
    {
        string apiKey = secrets.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
            return Results.BadRequest(new { error = "尚未配置 Gemini API Key，请先在上方的机密卡片中填写并保存" });

        try
        {
            GeminiConfig currentConfig = config.Value;
            IReadOnlyList<GeminiModelInfo> models = await api.ListModelsAsync(
                currentConfig.BaseUrl,
                currentConfig.ProxyUrl,
                apiKey,
                cancellationToken);
            GeminiModelOptionView[] views =
            [
                .. models.Select(model => new GeminiModelOptionView
                {
                    Id             = model.Id,
                    DisplayName    = model.DisplayName,
                    Description    = model.Description,
                    Thinking       = model.Thinking,
                    AllowedEfforts = model.AllowedEfforts
                })
            ];
            return Results.Ok(views);
        }
        catch (GeminiApiException exception)
        {
            return Results.Json(new { error = $"拉取模型列表失败：{exception.Message}" }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Results.Json(new { error = "拉取模型列表超时，请检查 Base URL、代理与网络连接" }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }

    /// <summary>查询指定时间窗口内的用量统计（成本 USD、请求数、token 消耗与缓存命中率）。</summary>
    internal static async Task<IResult> UsageAsync(GeminiUsageStore store, int? days)
    {
        int window = days is null ? 30 : days.Value <= 0 ? 0 : Math.Clamp(days.Value, 1, 3650);
        IReadOnlyList<GeminiUsageRecord> rows = await store.QueryAsync(window);

        GeminiUsageMetricsView totals = Aggregate(rows);
        GeminiModelUsageView[] byModel =
        [
            .. rows.GroupBy(row => row.Model, StringComparer.OrdinalIgnoreCase)
                   .Select(group => new GeminiModelUsageView
                   {
                       Model   = group.First().Model,
                       Metrics = Aggregate(group)
                   })
                   .OrderByDescending(item => item.Metrics.CostUsd)
                   .ThenByDescending(item => item.Metrics.RequestCount)
        ];
        GeminiDailyUsageView[] daily =
        [
            .. rows.GroupBy(row => row.StatDate, StringComparer.Ordinal)
                   .Select(group => new GeminiDailyUsageView
                   {
                       Date    = group.Key,
                       Metrics = Aggregate(group)
                   })
                   .OrderByDescending(item => item.Date, StringComparer.Ordinal)
        ];

        return Results.Ok(
            new GeminiUsageSummaryView
            {
                Days    = window,
                Totals  = totals,
                ByModel = byModel,
                Daily   = daily
            });
    }

    private static GeminiUsageMetricsView Aggregate(IEnumerable<GeminiUsageRecord> rows)
    {
        long requests = 0;
        long successes = 0;
        long prompt = 0;
        long cached = 0;
        long candidates = 0;
        long thoughts = 0;
        long total = 0;
        decimal cost = 0m;

        foreach (GeminiUsageRecord row in rows)
        {
            requests   += row.RequestCount;
            successes  += row.SuccessCount;
            prompt     += row.PromptTokens;
            cached     += row.CachedTokens;
            candidates += row.CandidatesTokens;
            thoughts   += row.ThoughtsTokens;
            total      += row.TotalTokens;
            cost       += row.CostUsd;
        }

        double cacheHitRate = prompt > 0 ? Math.Clamp((double)cached / prompt, 0, 1) : 0;
        return new GeminiUsageMetricsView
        {
            RequestCount     = requests,
            SuccessCount     = successes,
            FailedCount      = Math.Max(0, requests - successes),
            PromptTokens     = prompt,
            CachedTokens     = cached,
            CandidatesTokens = candidates,
            ThoughtsTokens   = thoughts,
            TotalTokens      = total,
            CacheHitRate     = cacheHitRate,
            CostUsd          = Math.Round(cost, 6, MidpointRounding.AwayFromZero)
        };
    }

    private static GeminiScopeKind? ParseKind(string kind) =>
        kind switch
        {
            "group"   => GeminiScopeKind.Group,
            "private" => GeminiScopeKind.Private,
            _         => null
        };

    private static async Task<IReadOnlyList<GeminiScopeCandidateView>> GroupsAsync(IBotAccessor bot, CancellationToken cancellationToken)
    {
        IReadOnlyList<GroupInfo> groups = bot.Api is { } api && await api.GetGroupListAsync(false, cancellationToken) is { IsSuccess: true, Data: { } list }
            ? list
            : [];
        return
        [
            .. groups.OrderBy(group => group.GroupName, StringComparer.CurrentCulture)
                     .Select(group => new GeminiScopeCandidateView
                     {
                         Id   = group.GroupId,
                         Name = group.GroupName
                     })
        ];
    }

    private static async Task<IReadOnlyList<GeminiScopeCandidateView>> FriendsAsync(IBotAccessor bot, CancellationToken cancellationToken)
    {
        IReadOnlyList<FriendInfo> friends = bot.Api is { } api && await api.GetFriendListAsync(false, cancellationToken) is { IsSuccess: true, Data: { } list }
            ? list
            : [];
        return
        [
            .. friends.Select(friend => new GeminiScopeCandidateView
                      {
                          Id   = friend.UserId,
                          Name = string.IsNullOrEmpty(friend.Remark) ? friend.Nickname : friend.Remark
                      })
                      .OrderBy(candidate => candidate.Name, StringComparer.CurrentCulture)
        ];
    }
}
