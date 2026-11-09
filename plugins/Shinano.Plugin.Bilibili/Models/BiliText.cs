namespace Shinano.Plugin.Bilibili.Models;

/// <summary>富文本的一个片段。</summary>
public sealed class BiliText
{
    /// <summary>显示方式。</summary>
    public required BiliTextKind Kind { get; init; }

    /// <summary>文字。</summary>
    public required string Text { get; init; }
}
