namespace Shinano.Framework.Rendering.Models;

/// <summary>长图中的一行。</summary>
public sealed class TextImageRow
{
    /// <summary>行首序号；为 null 时不显示序号列。</summary>
    public int? Index { get; init; }

    /// <summary>正文上方的灰色小标签，如"原因"。</summary>
    public string? Label { get; init; }

    /// <summary>正文前的胶囊标签，如"RG"。</summary>
    public string? Tag { get; init; }

    /// <summary>正文，按宽度自动换行，保留文中的换行符；过长时截断并以省略号结尾。</summary>
    public required string Text { get; init; }
}
