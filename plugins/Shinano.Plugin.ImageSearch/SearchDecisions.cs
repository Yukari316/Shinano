using Shinano.Plugin.ImageSearch.Models;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Models;

namespace Shinano.Plugin.ImageSearch;

/// <summary>
/// 综合各搜索服务的结果决定回复，每个服务都取相似度最高的一条（结果已按相似度降序排列）。搜图以 SauceNAO 为主、IQDB 兜底；
/// 搜番以 trace.moe 为主、SauceNAO 的番剧库兜底。主服务完成搜索时总是回复其最高相似度的结果，低于阈值只标为警告色；
/// 兜底只在主服务不可用（出错、额度用完或未配置）时查询，规则相同。都没有结果时按原因回复文案。
/// 取原图时的 Pixiv 优先由 <see cref="ImageResolver" /> 负责。
/// </summary>
public static class SearchDecisions
{
    /// <summary>主服务不可用，需要查兜底服务。</summary>
    public static bool NeedsFallback(SearchResult primary) => primary.Status != SearchStatus.Ok;

    /// <summary>搜图的回复。</summary>
    /// <param name="sauceNao">SauceNAO 的结果。</param>
    /// <param name="iqdb">IQDB 的结果；没有查询时为 null。</param>
    /// <param name="sauceNaoThreshold">SauceNAO 相似度阈值。</param>
    /// <param name="iqdbThreshold">IQDB 相似度阈值。</param>
    public static SearchDecision ForImage(SearchResult sauceNao, SearchResult? iqdb, double sauceNaoThreshold, double iqdbThreshold) =>
        Decide(sauceNao, sauceNao.Hits.FirstOrDefault(), sauceNaoThreshold, iqdb, iqdb?.Hits.FirstOrDefault(), iqdbThreshold);

    /// <summary>搜番的回复。</summary>
    /// <param name="traceMoe">trace.moe 的结果。</param>
    /// <param name="sauceNao">SauceNAO 的结果，只取番剧库的结果；没有查询时为 null。</param>
    /// <param name="animeThreshold">trace.moe 相似度阈值。</param>
    /// <param name="sauceNaoThreshold">SauceNAO 相似度阈值。</param>
    public static SearchDecision ForAnime(SearchResult traceMoe, SearchResult? sauceNao, double animeThreshold, double sauceNaoThreshold) =>
        Decide(
            traceMoe,
            traceMoe.Hits.FirstOrDefault(),
            animeThreshold,
            sauceNao,
            sauceNao?.Hits.Where(hit => hit.Kind == HitKind.Anime).MaxBy(hit => hit.Similarity),
            sauceNaoThreshold);

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
