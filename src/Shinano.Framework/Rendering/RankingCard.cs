namespace Shinano.Framework.Rendering;

/// <summary>排行卡片：前三名为并列的突出格，其余逐行列出。显示几名由调用方截取。</summary>
public sealed class RankingCard
{
    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>副标题，如「本群 12 人上榜」；没有时为 null。</summary>
    public string? Subtitle { get; init; }

    /// <summary>按名次排列的条目。</summary>
    public required IReadOnlyList<RankingEntry> Entries { get; init; }

    /// <summary>页脚右侧的自定义文字；为 null 时不显示。</summary>
    public string? FooterRight { get; init; }
}

/// <summary>排行中的一名。</summary>
public sealed class RankingEntry
{
    /// <summary>名称，过长时截断。</summary>
    public required string Name { get; init; }

    /// <summary>计数。</summary>
    public required long Count { get; init; }
}
