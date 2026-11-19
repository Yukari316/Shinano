namespace Shinano.Infrastructure.Logging;

/// <summary>内存日志流中的一条已渲染日志。</summary>
public sealed class LogEntry
{
    /// <summary>进程内单调递增的序号，用于订阅方去重与断点续读。</summary>
    public required long Sequence { get; init; }

    /// <summary>日志时间。</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Serilog 等级名称，如 <c>Information</c>。</summary>
    public required string Level { get; init; }

    /// <summary>日志类别（SourceContext），没有时为空字符串。</summary>
    public required string Category { get; init; }

    /// <summary>渲染后的消息文本。</summary>
    public required string Message { get; init; }

    /// <summary>按消息模板拆分的片段，依次拼接即为 <see cref="Message" />。</summary>
    public required IReadOnlyList<LogSegment> Segments { get; init; }

    /// <summary>异常文本，没有异常时为 null。</summary>
    public string? Exception { get; init; }
}

/// <summary>日志消息中的一段，面板按种类为参数着色。</summary>
public sealed class LogSegment
{
    /// <summary>渲染后的文字，与完整消息中的对应部分相同。</summary>
    public required string Text { get; init; }

    /// <summary>片段种类。</summary>
    public required LogSegmentKind Kind { get; init; }
}

/// <summary>日志消息片段的种类：模板中的普通文字，或按值类型区分的参数。</summary>
public enum LogSegmentKind
{
    /// <summary>消息模板中的普通文字。</summary>
    Text,

    /// <summary>字符串参数。</summary>
    String,

    /// <summary>数值参数。</summary>
    Number,

    /// <summary>布尔参数。</summary>
    Boolean,

    /// <summary>空值参数。</summary>
    Null,

    /// <summary>其他参数（枚举、时间、集合、结构等）。</summary>
    Other
}
