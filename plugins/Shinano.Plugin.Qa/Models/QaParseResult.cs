using Sora.Entities.Segments;

namespace Shinano.Plugin.Qa.Models;

/// <summary>录入消息的解析结果。</summary>
public sealed class QaParseResult
{
    /// <summary>无法解析的原因；成功时为 <see cref="QaParseError.None" />。</summary>
    public QaParseError Error { get; init; }

    /// <summary>问题文本（已去掉首尾空白）。</summary>
    public string Question { get; init; } = string.Empty;

    /// <summary>文字录入的回答片段；引用录入时为空，回答来自被引用的消息。</summary>
    public IReadOnlyList<QaDraftPart> Answer { get; init; } = [];

    /// <summary>引用录入时被引用的消息。</summary>
    public ReplySegment? Reply { get; init; }

    /// <summary>失败结果。</summary>
    public static QaParseResult Fail(QaParseError error) => new() { Error = error };
}
