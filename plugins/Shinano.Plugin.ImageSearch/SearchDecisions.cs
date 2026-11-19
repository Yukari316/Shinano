using Shinano.Plugin.ImageSources;

namespace Shinano.Plugin.ImageSearch;

/// <summary>
/// 综合各搜索服务的结果决定回复，每个服务都取相似度最高的一条（结果已按相似度降序排列，且只含请求时限定的库，
/// 见 <see cref="SearchDatabases" />）；搜图的 SauceNAO 结果 Pixiv 优先（见 <see cref="HitPicker" />）。
/// 搜图以 SauceNAO 的插画图库为主、IQDB 兜底；搜番以 trace.moe 为主、SauceNAO 的番剧库兜底。
/// 主服务完成搜索时总是回复其最高相似度的结果，低于阈值只标为警告色；
/// 兜底只在主服务不可用（出错、额度用完或未配置）时查询，规则相同。都没有结果时按原因回复文案。
/// 取原图时的 Pixiv 优先由 <see cref="ImageResolver" /> 负责。
/// </summary>
public static class SearchDecisions
{
    /// <summary>主服务不可用，需要查兜底服务。</summary>
    public static bool NeedsFallback(SearchResult primary) => primary.Status != SearchStatus.Ok;

    /// <summary>
    /// SauceNAO 的插画图库完成搜索，但没有结果或最高结果低于阈值，需要再检索全部库：部分作品（如 AI 作品）不在这些图库中。
    /// </summary>
    public static bool NeedsFullSearch(SearchResult illustrations, double threshold) =>
        illustrations.Status == SearchStatus.Ok && (illustrations.Hits.FirstOrDefault()?.Similarity ?? 0) < threshold;

    /// <summary>
    /// 合并插画图库与全部库的结果，按相似度从高到低排列，相似度相同时插画图库的结果在前（能取原图）；
    /// 全部库没有完成搜索（如短时额度用完）时只用插画图库的结果。
    /// </summary>
    public static SearchResult Merge(SearchResult illustrations, SearchResult all) =>
        all.Status != SearchStatus.Ok
            ? illustrations
            : new SearchResult
            {
                Status = SearchStatus.Ok,
                Hits   = [.. illustrations.Hits.Concat(all.Hits).OrderByDescending(hit => hit.Similarity)],
                Detail = all.Detail
            };

    /// <summary>
    /// 结果是否来自能取原图的库：SauceNAO 的结果须在插画图库中，其他搜索服务的结果在请求时已限定。
    /// 不在其中的（检索全部库得到的）只发基础信息卡，不查来源作品、不取原图。
    /// </summary>
    public static bool CanFetchOriginal(SearchHit hit) =>
        hit is not { Engine: SearchEngine.SauceNao, DatabaseId: { } database } || SearchDatabases.SauceNaoIllustrations.Contains((SauceNaoIndex)database);

    /// <summary>搜图的回复。</summary>
    /// <param name="sauceNao">SauceNAO 的结果。</param>
    /// <param name="iqdb">IQDB 的结果；没有查询时为 null。</param>
    /// <param name="sauceNaoThreshold">SauceNAO 相似度阈值。</param>
    /// <param name="iqdbThreshold">IQDB 相似度阈值。</param>
    public static SearchDecision ForImage(SearchResult sauceNao, SearchResult? iqdb, double sauceNaoThreshold, double iqdbThreshold) =>
        Decide(sauceNao, HitPicker.Pick(sauceNao.Hits, sauceNaoThreshold), sauceNaoThreshold, iqdb, iqdb?.Hits.FirstOrDefault(), iqdbThreshold);

    /// <summary>搜番的回复。</summary>
    /// <param name="traceMoe">trace.moe 的结果。</param>
    /// <param name="sauceNao">SauceNAO 番剧库的结果；没有查询时为 null。</param>
    /// <param name="animeThreshold">trace.moe 相似度阈值。</param>
    /// <param name="sauceNaoThreshold">SauceNAO 相似度阈值。</param>
    public static SearchDecision ForAnime(SearchResult traceMoe, SearchResult? sauceNao, double animeThreshold, double sauceNaoThreshold) =>
        Decide(traceMoe, traceMoe.Hits.FirstOrDefault(), animeThreshold, sauceNao, sauceNao?.Hits.FirstOrDefault(), sauceNaoThreshold);

    private static SearchDecision Decide(
        SearchResult  primary,
        SearchHit?    primaryHit,
        double        primaryThreshold,
        SearchResult? fallback,
        SearchHit?    fallbackHit,
        double        fallbackThreshold)
    {
        if (primary.Status == SearchStatus.Ok)
            return primaryHit is null ? Reply(ReplyTexts.NOT_FOUND) : Hit(primaryHit, primaryThreshold);
        if (fallback?.Status == SearchStatus.Ok)
            return fallbackHit is null ? Reply(ReplyTexts.NOT_FOUND) : Hit(fallbackHit, fallbackThreshold);
        return Reply(ReplyFor(primary.Status));
    }

    private static SearchDecision Hit(SearchHit hit, double threshold) =>
        new()
        {
            Hit           = hit,
            Threshold     = threshold,
            LowSimilarity = hit.Similarity < threshold
        };

    private static SearchDecision Reply(string text) => new() { Reply = text };

    // 主服务与兜底都不可用时按主服务的原因回复。
    private static string ReplyFor(SearchStatus primary) =>
        primary switch
        {
            SearchStatus.RateLimited  => ReplyTexts.RATE_LIMITED,
            SearchStatus.Unconfigured => ReplyTexts.UNCONFIGURED,
            _                         => ReplyTexts.SERVER_ERROR
        };
}

/// <summary>综合各搜索服务结果后的回复决定：回复一条结果，或回复一句文案。</summary>
public sealed class SearchDecision
{
    /// <summary>要回复的结果；为 null 时回复 <see cref="Reply" />。</summary>
    public SearchHit? Hit { get; init; }

    /// <summary>结果所属搜索服务的相似度阈值，单位百分比，显示在卡片的相似度刻度上。</summary>
    public double Threshold { get; init; }

    /// <summary>结果的相似度低于阈值，卡片以警告色显示。</summary>
    public bool LowSimilarity { get; init; }

    /// <summary>没有结果时的回复文案。</summary>
    public string Reply { get; init; } = string.Empty;
}
