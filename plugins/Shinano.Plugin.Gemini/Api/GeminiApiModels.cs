namespace Shinano.Plugin.Gemini.Api;

/// <summary>从 Gemini API 拉取到的单个可用模型信息。</summary>
public sealed class GeminiModelInfo
{
    /// <summary>模型 ID（不含 <c>models/</c> 前缀）。</summary>
    public required string Id { get; init; }

    /// <summary>显示名称。</summary>
    public required string DisplayName { get; init; }

    /// <summary>模型简介。</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>API 报告是否支持思考；部分中转未返回该字段时为 null。</summary>
    public bool? Thinking { get; init; }

    /// <summary>该模型可选的 effort 列表；为空表示不支持思考配置。</summary>
    public required IReadOnlyList<string> AllowedEfforts { get; init; }
}

/// <summary>内联多媒体数据（当前用于图片）。</summary>
public sealed class GeminiMediaPart
{
    /// <summary>MIME 类型（如 <c>image/jpeg</c>）。</summary>
    public required string MediaType { get; init; }

    /// <summary>原始字节。</summary>
    public required byte[] Data { get; init; }
}

/// <summary>对话历史中的单个消息段。</summary>
public sealed class GeminiConversationPart
{
    /// <summary>文本内容。</summary>
    public string? Text { get; init; }

    /// <summary>内联媒体内容。</summary>
    public GeminiMediaPart? Media { get; init; }

    /// <summary>Gemini 3/4 模型返回并要求在多轮历史中回传的思考签名。</summary>
    public string? ThoughtSignature { get; init; }
}

/// <summary>对话历史中的一条消息（<c>user</c> 或 <c>model</c>）。</summary>
public sealed class GeminiConversationMessage
{
    /// <summary>角色（<c>user</c> 或 <c>model</c>）。</summary>
    public required string Role { get; init; }

    /// <summary>消息段列表。</summary>
    public required IReadOnlyList<GeminiConversationPart> Parts { get; init; }
}

/// <summary>调用 Gemini <c>generateContent</c> 的完整请求参数。</summary>
public sealed class GeminiGenerateRequest
{
    /// <summary>API 根地址。</summary>
    public required string BaseUrl { get; init; }

    /// <summary>可选代理地址。</summary>
    public string ProxyUrl { get; init; } = string.Empty;

    /// <summary>API Key。</summary>
    public required string ApiKey { get; init; }

    /// <summary>模型 ID。</summary>
    public required string Model { get; init; }

    /// <summary>思考深度配置值。</summary>
    public string Effort { get; init; } = string.Empty;

    /// <summary>可选系统提示词。</summary>
    public string SystemPrompt { get; init; } = string.Empty;

    /// <summary>是否请求模型返回思考摘要。</summary>
    public bool IncludeThoughts { get; init; }

    /// <summary>是否开启 Google Search Grounding。</summary>
    public bool EnableGoogleSearch { get; init; }

    /// <summary>参考来源最大返回条数。</summary>
    public int MaxSearchSources { get; init; } = 5;

    /// <summary>请求超时秒数。</summary>
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>包含历史轮次与本轮提问的完整消息列表。</summary>
    public required IReadOnlyList<GeminiConversationMessage> Messages { get; init; }
}

/// <summary>Google Search Grounding 返回的一条去重参考来源。</summary>
public sealed class GeminiSearchSource
{
    /// <summary>来源标题。</summary>
    public required string Title { get; init; }

    /// <summary>来源域名（无法解析时为空）。</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>原始链接。</summary>
    public string Uri { get; init; } = string.Empty;
}

/// <summary>Gemini 响应中的 token 用量统计。</summary>
public sealed class GeminiUsageMetadata
{
    /// <summary>输入（提示词与上下文，含缓存命中部分）token 数。</summary>
    public int? PromptTokens { get; init; }

    /// <summary>命中隐式/显式缓存的输入 token 数（未命中或未返回时为 null）。</summary>
    public int? CachedTokens { get; init; }

    /// <summary>模型可见回答输出 token 数。</summary>
    public int? CandidatesTokens { get; init; }

    /// <summary>模型思考过程消耗 token 数（未返回时为 null）。</summary>
    public int? ThoughtsTokens { get; init; }

    /// <summary>总计 token 数。</summary>
    public int? TotalTokens { get; init; }

    /// <summary>本次实际生成的输出 token 数（含思考 token），用于计算生成速度与输出成本。</summary>
    public int OutputTokens
    {
        get
        {
            if (CandidatesTokens.HasValue || ThoughtsTokens.HasValue)
                return (CandidatesTokens ?? 0) + (ThoughtsTokens ?? 0);
            if (TotalTokens.HasValue && PromptTokens.HasValue)
                return Math.Max(0, TotalTokens.Value - PromptTokens.Value);
            return 0;
        }
    }

    /// <summary>按给定耗时计算输出 token 生成速度（tokens/s）。</summary>
    public double TokensPerSecond(TimeSpan elapsed) =>
        elapsed.TotalSeconds > 0 && OutputTokens > 0 ? OutputTokens / elapsed.TotalSeconds : 0;

    /// <summary>格式化供日志输出的用量、速度与可选估算成本摘要。</summary>
    public string FormatSummary(TimeSpan elapsed, decimal? costUsd = null)
    {
        List<string> items = [];
        if (PromptTokens.HasValue)
        {
            items.Add(CachedTokens is > 0
                ? $"输入 {PromptTokens.Value}（缓存 {CachedTokens.Value}）"
                : $"输入 {PromptTokens.Value}");
        }

        if (CandidatesTokens.HasValue) items.Add($"输出 {CandidatesTokens.Value}");
        if (ThoughtsTokens is > 0) items.Add($"思考 {ThoughtsTokens.Value}");
        if (TotalTokens.HasValue) items.Add($"总计 {TotalTokens.Value}");

        string tokenText = items.Count > 0 ? $"{string.Join(" / ", items)} tokens" : "未返回 token 明细";
        double speed = TokensPerSecond(elapsed);
        string summary = speed > 0
            ? $"{tokenText}，速度 {speed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} tok/s"
            : tokenText;
        if (costUsd is > 0m)
            summary += $"，费用 ${costUsd.Value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture)}";
        return summary;
    }
}

/// <summary>Gemini <c>generateContent</c> 解析结果。</summary>
public sealed class GeminiGenerateResponse
{
    /// <summary>模型最终回答 Markdown 文本。</summary>
    public required string AnswerText { get; init; }

    /// <summary>可选思考摘要文本（未开启或模型未返回时为空）。</summary>
    public string ThoughtSummary { get; init; } = string.Empty;

    /// <summary>需保留在多轮上下文中的 model 消息段（保留 <c>thoughtSignature</c>）。</summary>
    public required IReadOnlyList<GeminiConversationPart> ModelParts { get; init; }

    /// <summary>联网搜索去重后的参考来源列表。</summary>
    public IReadOnlyList<GeminiSearchSource> Sources { get; init; } = [];

    /// <summary>上游返回的 token 用量统计；未返回时为 null。</summary>
    public GeminiUsageMetadata? Usage { get; init; }

    /// <summary>本次 API 调用耗时。</summary>
    public TimeSpan Elapsed { get; init; }
}

/// <summary>Gemini REST API 返回非成功状态或响应无效时的异常。</summary>
public sealed class GeminiApiException : Exception
{
    /// <summary>创建异常。</summary>
    public GeminiApiException(string message, int? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>上游 HTTP 状态码；网络或解析错误时为 null。</summary>
    public int? StatusCode { get; }
}
