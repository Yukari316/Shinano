namespace Shinano.Framework.Rendering;

/// <summary>
/// 文档卡片：把 Markdown 渲染为图片，没有标题块。支持 CommonMark、GFM（表格、删除线、任务列表）、
/// 终末地暗色代码块语法高亮与行内/块级 LaTeX 数学公式；HTML 原样显示为文本，单条公式失败回退为文本。
/// </summary>
public sealed class DocumentCard
{
    /// <summary>Markdown 文本。</summary>
    public required string Markdown { get; init; }

    /// <summary>
    /// 文中 <c>![说明](键名)</c> 引用的图片，键为链接部分；渲染器不联网下载。找不到或无法解码的图片只显示说明。
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> Images { get; init; } = new Dictionary<string, byte[]>();

    /// <summary>图片的处理方式。</summary>
    public DocumentImageMode ImageMode { get; init; } = DocumentImageMode.Inline;

    /// <summary>是否在链接文字后附上 URL；默认只显示带下划线的文字，避免发出的图片中链接过多。</summary>
    public bool ShowLinkUrls { get; init; }

    /// <summary>页脚左侧的说明，如内容来源；没有时为 null。</summary>
    public string? Note { get; init; }

    /// <summary>页脚右侧的自定义文字；为 null 时不显示。</summary>
    public string? FooterRight { get; init; }
}

/// <summary>文档中图片的处理方式。</summary>
public enum DocumentImageMode
{
    /// <summary>按正文宽度等比缩小（不放大）画进文档图，说明写在图下。</summary>
    Inline,

    /// <summary>原位画「图 n」占位，图片另行返回，由调用方随文档图之后发送。</summary>
    Separate
}

/// <summary>文档卡片的渲染结果。</summary>
public sealed class RenderedDocument
{
    /// <summary>文档图（PNG）。</summary>
    public required byte[] Image { get; init; }

    /// <summary>
    /// <see cref="DocumentImageMode.Separate" /> 时按「图 n」顺序返回的原始图片数据，由调用方随文档图之后发送；
    /// 内嵌时为空。
    /// </summary>
    public IReadOnlyList<byte[]> Attachments { get; init; } = [];
}
