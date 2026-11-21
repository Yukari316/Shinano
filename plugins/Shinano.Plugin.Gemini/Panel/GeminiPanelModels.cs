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
