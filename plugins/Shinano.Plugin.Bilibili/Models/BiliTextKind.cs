namespace Shinano.Plugin.Bilibili.Models;

/// <summary>富文本片段的显示方式。</summary>
public enum BiliTextKind
{
    /// <summary>普通文字。</summary>
    Plain,

    /// <summary>高亮文字：@、话题、链接、视频号等可点击内容，卡片中不附链接，只改颜色。</summary>
    Highlight,

    /// <summary>B站表情（如 <c>[doge]</c>），显示其文字。</summary>
    Emoji
}
