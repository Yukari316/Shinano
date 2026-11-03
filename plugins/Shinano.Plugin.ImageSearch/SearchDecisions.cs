using Shinano.Plugin.ImageSearch.Delivery;
using Shinano.Plugin.ImageSearch.Models;

namespace Shinano.Plugin.ImageSearch;

/// <summary>
/// 综合各搜索服务的结果决定回复，每个服务都取相似度最高的一条（结果已按相似度降序排列）。搜图以 SauceNAO 为主，
/// 没有达到阈值的结果时以 IQDB 兜底；搜番以 trace.moe 为主，以 SauceNAO 的番剧库兜底。兜底也没有可信结果时，
/// 回复主服务低于阈值的结果（黄色竖条），都没有结果时按原因回复文案。取原图时的 Pixiv 优先由 <see cref="ImageResolver" /> 负责。
/// </summary>
public static class SearchDecisions
{
    /// <summary>SauceNAO 没有达到阈值的结果，需要查 IQDB。</summary>
    public static bool NeedsIqdb(SearchResult sauceNao, double threshold) =>
        sauceNao.Status != SearchStatus.Ok || sauceNao.Hits.FirstOrDefault() is not { } hit || hit.Similarity < threshold;

    /// <summary>trace.moe 没有达到阈值的结果，需要查 SauceNAO 的番剧库。</summary>
    public static bool NeedsSauceNaoAnime(SearchResult traceMoe, double threshold) =>
        traceMoe.Status != SearchStatus.Ok || traceMoe.Hits.FirstOrDefault() is not { } hit || hit.Similarity < threshold;

    /// <summary>搜图的回复。</summary>
    /// <param name="sauceNao">SauceNAO 的结果。</param>
    /// <param name="iqdb">IQDB 的结果；没有查询时为 null。</param>
    /// <param name="sauceNaoThreshold">SauceNAO 相似度阈值。</param>
    /// <param name="iqdbThreshold">IQDB 相似度阈值，低于阈值视为没有结果。</param>
    public static SearchDecision ForImage(SearchResult sauceNao, SearchResult? iqdb, double sauceNaoThreshold, double iqdbThreshold)
    {
        SearchHit? primary = sauceNao.Status == SearchStatus.Ok ? sauceNao.Hits.FirstOrDefault() : null;
        SearchHit? fallback = iqdb?.Status == SearchStatus.Ok ? iqdb.Hits.FirstOrDefault() : null;
        return Decide(primary, sauceNaoThreshold, fallback, iqdbThreshold, sauceNao, iqdb);
    }

    /// <summary>搜番的回复。</summary>
    /// <param name="traceMoe">trace.moe 的结果。</param>
    /// <param name="sauceNao">SauceNAO 的结果，只取番剧库的结果；没有查询时为 null。</param>
    /// <param name="animeThreshold">trace.moe 相似度阈值。</param>
    /// <param name="sauceNaoThreshold">SauceNAO 相似度阈值。</param>
    public static SearchDecision ForAnime(SearchResult traceMoe, SearchResult? sauceNao, double animeThreshold, double sauceNaoThreshold)
    {
        SearchHit? primary = traceMoe.Status == SearchStatus.Ok ? traceMoe.Hits.FirstOrDefault() : null;
        SearchHit? fallback = sauceNao?.Status == SearchStatus.Ok
            ? sauceNao.Hits.Where(hit => hit.Kind == HitKind.Anime).MaxBy(hit => hit.Similarity)
            : null;
        return Decide(primary, animeThreshold, fallback, sauceNaoThreshold, traceMoe, sauceNao);
    }

    private static SearchDecision Decide(
        SearchHit?    primary,
        double        primaryThreshold,
        SearchHit?    fallback,
        double        fallbackThreshold,
        SearchResult  primaryResult,
        SearchResult? fallbackResult)
    {
        if (primary is not null && primary.Similarity >= primaryThreshold)
            return new SearchDecision
            {
                Hit       = primary,
                Threshold = primaryThreshold
            };
        if (fallback is not null && fallback.Similarity >= fallbackThreshold)
            return new SearchDecision
            {
                Hit       = fallback,
                Threshold = fallbackThreshold
            };
        if (primary is not null)
            return new SearchDecision
            {
                Hit           = primary,
                Threshold     = primaryThreshold,
                LowSimilarity = true
            };

        return new SearchDecision { Reply = ReplyFor(primaryResult.Status, fallbackResult?.Status) };
    }

    // 主服务完成了搜索只是没有结果时回复没找到；主服务出错时，兜底完成了搜索同样算没找到。
    private static string ReplyFor(SearchStatus primary, SearchStatus? fallback) =>
        primary switch
        {
            SearchStatus.Ok           => ReplyTexts.NOT_FOUND,
            SearchStatus.RateLimited  => ReplyTexts.RATE_LIMITED,
            SearchStatus.Unconfigured => ReplyTexts.UNCONFIGURED,
            _ when fallback == SearchStatus.Ok => ReplyTexts.NOT_FOUND,
            _                         => ReplyTexts.SERVER_ERROR
        };
}
