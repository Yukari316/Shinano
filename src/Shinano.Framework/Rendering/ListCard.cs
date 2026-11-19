namespace Shinano.Framework.Rendering;

/// <summary>列表卡片：如有人问的词条列表，一张为一页。</summary>
public sealed class ListCard
{
    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>副标题，如「本群共 47 条」；没有时为 null。</summary>
    public string? Subtitle { get; init; }

    /// <summary>当前页码，从 1 开始。</summary>
    public required int Page { get; init; }

    /// <summary>总页数。</summary>
    public required int Pages { get; init; }

    /// <summary>本页各项。</summary>
    public required IReadOnlyList<ListCardItem> Items { get; init; }

    /// <summary>页脚左侧的提示，如翻页方法；没有时为 null。</summary>
    public string? Hint { get; init; }

    /// <summary>页脚右侧的自定义文字；为 null 时不显示。</summary>
    public string? FooterRight { get; init; }
}

/// <summary>列表中的一项。</summary>
public sealed class ListCardItem
{
    /// <summary>序号，跨页连续由调用方给出。</summary>
    public required int Index { get; init; }

    /// <summary>正文前的短标签，如 <c>RG</c>；没有时为 null。</summary>
    public string? Tag { get; init; }

    /// <summary>正文，过长时换行。</summary>
    public required string Text { get; init; }
}
