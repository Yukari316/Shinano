using System.Text.Json;
using System.Text.Json.Nodes;
using Shinano.Plugin.ImageSearch.Models;

namespace Shinano.Plugin.ImageSearch.Parsing;

/// <summary>
/// 解析经图源服务转发的 trace.moe 响应（网关固定带 <c>anilistInfo</c>）。相似度为 0–1，额度用完或并发超限时上游返回 402。
/// 番名优先取 AniList 的 <c>chinese</c>（繁体，原样显示），其次日文名与罗马字。
/// </summary>
public static class TraceMoeParser
{
    private const int    PAYMENT_REQUIRED = 402;
    private const int    OK               = 200;
    private const double PERCENT          = 100;
    private const string SOURCE_NAME      = "trace.moe";

    /// <summary>解析搜索结果，按相似度从高到低排列。</summary>
    public static SearchResult Parse(GatewayResponse response)
    {
        switch (response.Outcome)
        {
            case GatewayOutcome.Unauthorized or GatewayOutcome.Unconfigured:
                return SearchResult.Of(SearchStatus.Unconfigured, response.Body);
            case GatewayOutcome.Failed:
                return SearchResult.Of(SearchStatus.Failed, response.Body);
        }

        if (response.StatusCode == PAYMENT_REQUIRED) return SearchResult.Of(SearchStatus.RateLimited, response.Body);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(response.Body);
        }
        catch (JsonException)
        {
            return SearchResult.Of(SearchStatus.Failed, $"HTTP {response.StatusCode}，响应不是 JSON");
        }

        if (response.StatusCode != OK || JsonFields.Text(root?["error"]) is not null)
            return SearchResult.Of(SearchStatus.Failed, $"HTTP {response.StatusCode}：{JsonFields.Text(root?["error"])}");

        return new SearchResult
        {
            Status = SearchStatus.Ok,
            Hits   = [.. (root?["result"] as JsonArray ?? []).Select(ToHit).OfType<SearchHit>().OrderByDescending(hit => hit.Similarity)],
            Detail = $"已用额度 {JsonFields.Text(root?["quotaUsed"])}/{JsonFields.Text(root?["quota"])}"
        };
    }

    /// <summary>把秒数格式化为时间点，如 13:06 或 1:02:03。</summary>
    public static string FormatTime(double seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(Math.Floor(seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private static SearchHit? ToHit(JsonNode? result)
    {
        if (JsonFields.Number(result?["similarity"]) is not { } similarity) return null;

        JsonNode? anilist = result?["anilist"];
        JsonNode? title = anilist?["title"];
        return new SearchHit
        {
            Engine     = SearchEngine.TraceMoe,
            Similarity = similarity * PERCENT,
            SourceName = SOURCE_NAME,
            Kind       = HitKind.Anime,
            Adult      = anilist?["isAdult"]?.GetValueKind() == JsonValueKind.True,
            Title      = JsonFields.Text(title?["chinese"]) ?? JsonFields.Text(title?["native"]) ?? JsonFields.Text(title?["romaji"]) ?? JsonFields.Text(result?["filename"]),
            WorkId     = JsonFields.Text(anilist?["id"]),
            Episode    = JsonFields.Text(result?["episode"]),
            Time       = JsonFields.Number(result?["at"]) is { } at ? FormatTime(at) : null,
            PreviewUrl = JsonFields.Text(result?["image"])
        };
    }
}
