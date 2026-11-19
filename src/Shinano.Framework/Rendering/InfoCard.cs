namespace Shinano.Framework.Rendering;

/// <summary>信息卡片：如搜图结果、搜番结果、图库图片信息。</summary>
public sealed class InfoCard
{
    /// <summary>标题块左上的类型标签，只用英文字母、数字与常见符号，如 <c>SAUCENAO</c>。</summary>
    public required string Label { get; init; }

    /// <summary>标题块右上的说明，如 <c>ILLUST</c>；没有时为 null。</summary>
    public string? Meta { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>等级，如相似度低于阈值时为警告。</summary>
    public CardLevel Level { get; init; } = CardLevel.Normal;

    /// <summary>相似度，单位百分比；为 null 时不显示相似度刻度。</summary>
    public double? Similarity { get; init; }

    /// <summary>相似度阈值，单位百分比，显示为刻度上的虚线；为 null 时不画。</summary>
    public double? Threshold { get; init; }

    /// <summary>正文各行。</summary>
    public IReadOnlyList<CardRow> Rows { get; init; } = [];

    /// <summary>页脚左侧的说明；没有时为 null。</summary>
    public string? Note { get; init; }

    /// <summary>页脚右侧的自定义文字；为 null 时不显示。</summary>
    public string? FooterRight { get; init; }
}
