namespace Shinano.Framework.Rendering.Models;

/// <summary>宿主统一风格的文本长图：标题区、逐行内容与可选页脚，长度随内容增加。</summary>
public sealed class TextImageDocument
{
    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>标题下方的灰色副标题。</summary>
    public string? Subtitle { get; init; }

    /// <summary>等级，默认无。</summary>
    public TextImageLevel Level { get; init; } = TextImageLevel.None;

    /// <summary>内容行，按顺序绘制，以淡分隔线隔开；可以为空。</summary>
    public IReadOnlyList<TextImageRow> Rows { get; init; } = [];

    /// <summary>底部的灰色页脚。</summary>
    public string? Footer { get; init; }
}
