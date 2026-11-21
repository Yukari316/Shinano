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
