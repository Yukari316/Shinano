namespace Shinano.Plugin.Bilibili.Feed;

/// <summary>动态流的一页。</summary>
public sealed class BiliFeedPage
{
    /// <summary>本页动态，按接口返回的顺序（新到旧，置顶在前）。</summary>
    public required IReadOnlyList<BiliDynamic> Items { get; init; }

    /// <summary>下一页的偏移；没有更多时为 null。</summary>
    public string? NextOffset { get; init; }

    /// <summary>关注流用于查询有无更新的基线；动态空间页为 null。</summary>
    public string? UpdateBaseline { get; init; }
}
