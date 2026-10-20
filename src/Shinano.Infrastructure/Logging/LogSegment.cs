namespace Shinano.Infrastructure.Logging;

/// <summary>日志消息中的一段，面板按种类为参数着色。</summary>
public sealed class LogSegment
{
    /// <summary>渲染后的文字，与完整消息中的对应部分相同。</summary>
    public required string Text { get; init; }

    /// <summary>片段种类。</summary>
    public required LogSegmentKind Kind { get; init; }
}
