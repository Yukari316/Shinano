namespace Shinano.Framework.Rendering;

/// <summary>卡片中的一行：标签在左、内容在右，内容按 <see cref="Style" /> 显示。</summary>
public sealed class CardRow
{
    /// <summary>标签，如「原因」，宜在四个字以内。</summary>
    public required string Label { get; init; }

    /// <summary>内容的显示方式，默认为一段文字。</summary>
    public CardRowStyle Style { get; init; } = CardRowStyle.Text;

    /// <summary><see cref="CardRowStyle.Text" /> 时的内容，过长时换行。</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary><see cref="CardRowStyle.Items" /> 时的各个元素；单个元素宽于一行时截断并以省略号结尾。</summary>
    public IReadOnlyList<string> Items { get; init; } = [];

    /// <summary><see cref="CardRowStyle.Items" /> 时是否为每个元素画方框（如标签）。</summary>
    public bool ItemBorder { get; init; }
}

/// <summary>卡片行内容的显示方式。</summary>
public enum CardRowStyle
{
    /// <summary>一段文字（<see cref="CardRow.Text" />），过长时换行。</summary>
    Text,

    /// <summary>多个元素（<see cref="CardRow.Items" />）从左到右排列，放不下时换行，可为每个元素画方框（<see cref="CardRow.ItemBorder" />）。</summary>
    Items
}
