namespace Shinano.Framework.Rendering.Models;

/// <summary>卡片中的一行键值，标签在左、内容在右，内容过长时换行。</summary>
public sealed class CardRow
{
    /// <summary>标签，如「原因」，宜在四个字以内。</summary>
    public required string Label { get; init; }

    /// <summary>内容。</summary>
    public required string Text { get; init; }
}
