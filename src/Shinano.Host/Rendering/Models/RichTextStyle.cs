using SkiaSharp;

namespace Shinano.Host.Rendering.Models;

/// <summary>一块行内文字的基础样式。</summary>
internal sealed class RichTextStyle
{
    /// <summary>正文字体。</summary>
    public required SKTypeface Typeface { get; init; }

    /// <summary>粗体部分的字体。</summary>
    public required SKTypeface StrongTypeface { get; init; }

    /// <summary>字号（逻辑像素）。</summary>
    public required float Size { get; init; }

    /// <summary>行高与字号之比。</summary>
    public required float Leading { get; init; }

    /// <summary>正文颜色。</summary>
    public required SKColor Color { get; init; }
}
