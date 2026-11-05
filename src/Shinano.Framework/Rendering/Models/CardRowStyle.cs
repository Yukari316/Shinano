namespace Shinano.Framework.Rendering.Models;

/// <summary>卡片行内容的显示方式。</summary>
public enum CardRowStyle
{
    /// <summary>一段文字（<see cref="CardRow.Text" />），过长时换行。</summary>
    Text,

    /// <summary>多个元素（<see cref="CardRow.Items" />）从左到右排列，放不下时换行，可为每个元素画方框（<see cref="CardRow.ItemBorder" />）。</summary>
    Items
}
