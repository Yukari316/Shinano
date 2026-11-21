namespace Shinano.Plugin.Gemini.Panel;

/// <summary>群聊与好友私聊的响应白名单视图。</summary>
public sealed class GeminiScopesView
{
    /// <summary>群聊白名单。</summary>
    public required GeminiScopeView Group { get; init; }

    /// <summary>好友私聊白名单。</summary>
    public required GeminiScopeView Private { get; init; }
}

/// <summary>一类会话的响应白名单视图。</summary>
public sealed class GeminiScopeView
{
    /// <summary>允许响应的群号或好友 QQ 号。</summary>
    public long[] Ids { get; init; } = [];
}

/// <summary>保存一类会话白名单的请求体。</summary>
public sealed class GeminiScopeUpdate
{
    /// <summary>允许响应的群号或好友 QQ 号。</summary>
    public long[]? Ids { get; init; }
}

/// <summary>响应白名单的可选会话项。</summary>
public sealed class GeminiScopeCandidateView
{
    /// <summary>群号或好友 QQ 号。</summary>
    public required long Id { get; init; }

    /// <summary>群名或好友备注/昵称；Bot 已不在该群或已不是好友时为 null。</summary>
    public string? Name { get; init; }
}

/// <summary>当前生效的模型与思考深度视图。</summary>
public sealed class GeminiModelSettingsView
{
    /// <summary>模型 ID。</summary>
    public required string Model { get; init; }

    /// <summary>思考深度。</summary>
    public required string Effort { get; init; }

    /// <summary>当前模型允许选择的思考深度列表。</summary>
    public required IReadOnlyList<string> AllowedEfforts { get; init; }

    /// <summary>是否已配置 Gemini API Key。</summary>
    public required bool HasApiKey { get; init; }
}

/// <summary>保存模型与思考深度的请求体。</summary>
public sealed class GeminiModelSettingsUpdate
{
    /// <summary>模型 ID。</summary>
    public string? Model { get; init; }

    /// <summary>思考深度。</summary>
    public string? Effort { get; init; }

    /// <summary>可选：来自模型列表的 <c>thinking</c> 能力标记。</summary>
    public bool? Thinking { get; init; }
}

/// <summary>从 Gemini API 拉取到的模型项视图。</summary>
public sealed class GeminiModelOptionView
{
    /// <summary>模型 ID。</summary>
    public required string Id { get; init; }

    /// <summary>显示名称。</summary>
    public required string DisplayName { get; init; }

    /// <summary>模型简介。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>API 报告的是否支持思考。</summary>
    public bool? Thinking { get; init; }

    /// <summary>可选的 effort 列表。</summary>
    public required IReadOnlyList<string> AllowedEfforts { get; init; }
}

/// <summary>管理面板展示的用量统计汇总视图。</summary>
public sealed class GeminiUsageSummaryView
{
    /// <summary>统计天数窗口；0 表示全部历史。</summary>
    public required int Days { get; init; }

    /// <summary>汇总总览指标。</summary>
    public required GeminiUsageMetricsView Totals { get; init; }

    /// <summary>按模型分组的明细列表（按成本与请求数降序）。</summary>
    public required IReadOnlyList<GeminiModelUsageView> ByModel { get; init; }

    /// <summary>按日期分组的明细列表（按日期降序）。</summary>
    public required IReadOnlyList<GeminiDailyUsageView> Daily { get; init; }
}

/// <summary>一组用量汇总指标。</summary>
public sealed class GeminiUsageMetricsView
{
    /// <summary>总请求数。</summary>
    public required long RequestCount { get; init; }

    /// <summary>成功请求数。</summary>
    public required long SuccessCount { get; init; }

    /// <summary>失败请求数。</summary>
    public required long FailedCount { get; init; }

    /// <summary>总输入 token 数（含缓存命中）。</summary>
    public required long PromptTokens { get; init; }

    /// <summary>缓存命中 token 数。</summary>
    public required long CachedTokens { get; init; }

    /// <summary>可见输出 token 数。</summary>
    public required long CandidatesTokens { get; init; }

    /// <summary>思考 token 数。</summary>
    public required long ThoughtsTokens { get; init; }

    /// <summary>总计 token 数。</summary>
    public required long TotalTokens { get; init; }

    /// <summary>缓存命中率（0 到 1，输入为 0 时为 0）。</summary>
    public required double CacheHitRate { get; init; }

    /// <summary>估算成本（USD）。</summary>
    public required decimal CostUsd { get; init; }
}

/// <summary>按模型分组的用量行。</summary>
public sealed class GeminiModelUsageView
{
    /// <summary>模型 ID。</summary>
    public required string Model { get; init; }

    /// <summary>该模型的汇总指标。</summary>
    public required GeminiUsageMetricsView Metrics { get; init; }
}

/// <summary>按日期分组的用量行。</summary>
public sealed class GeminiDailyUsageView
{
    /// <summary>统计日期（<c>yyyy-MM-dd</c>）。</summary>
    public required string Date { get; init; }

    /// <summary>当日汇总指标。</summary>
    public required GeminiUsageMetricsView Metrics { get; init; }
}
