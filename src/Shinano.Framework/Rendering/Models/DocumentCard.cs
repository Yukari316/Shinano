namespace Shinano.Framework.Rendering.Models;

/// <summary>
/// 文档卡片：把 Markdown 渲染为图片，没有标题块。支持 CommonMark 与 GFM 表格、删除线、任务列表；
/// HTML 与公式原样显示为文本；代码块不做语法高亮。
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
