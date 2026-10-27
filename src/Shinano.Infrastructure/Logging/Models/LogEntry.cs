namespace Shinano.Infrastructure.Logging.Models;

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
