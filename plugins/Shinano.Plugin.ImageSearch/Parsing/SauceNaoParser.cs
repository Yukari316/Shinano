using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shinano.Plugin.ImageSearch.Models;

namespace Shinano.Plugin.ImageSearch.Parsing;

/// <summary>
/// 解析经图源服务转发的 SauceNAO 响应（<c>output_type=2</c>）。限额用尽时上游返回 429；
/// 正文 <c>header.status</c> 为 0 表示成功，负数为请求问题，正数为服务端问题。
/// </summary>
public static partial class SauceNaoParser
{
    private const int    ANIME_INDEX            = 21;
    private const int    ADULT_ANIME_INDEX      = 22;
    private const int    TOO_MANY_REQUESTS      = 429;
    private const int    FORBIDDEN              = 403;
    private const string UNKNOWN_SOURCE         = "SauceNAO";

    // H-Magazines、H-Game CG、FAKKU、H-Misc（nhentai）、H-Anime、E-Hentai：成人向来源，只发卡片。
    private static readonly int[] AdultIndexes = [0, 2, 16, 18, ADULT_ANIME_INDEX, 38];

    // 结果 data 中各图站的作品号字段。
    private static readonly (string Field, ImageSite Site)[] IdFields =
    [
        ("pixiv_id", ImageSite.Pixiv),
        ("danbooru_id", ImageSite.Danbooru),
        ("yandere_id", ImageSite.Yandere),
        ("gelbooru_id", ImageSite.Gelbooru),
        ("sankaku_id", ImageSite.Sankaku),
        ("konachan_id", ImageSite.Konachan)
    ];

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

        if (response.StatusCode == TOO_MANY_REQUESTS) return SearchResult.Of(SearchStatus.RateLimited, response.Body);
        // SauceNAO 以 403 表示 Key 无效或账号不允许使用 API，属于图源服务的配置问题。
        if (response.StatusCode == FORBIDDEN) return SearchResult.Of(SearchStatus.Unconfigured, response.Body);

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(response.Body);
        }
        catch (JsonException)
        {
            return SearchResult.Of(SearchStatus.Failed, $"HTTP {response.StatusCode}，响应不是 JSON");
        }

        JsonNode? header = root?["header"];
        if (JsonFields.Number(header?["status"]) is not 0)
            return SearchResult.Of(SearchStatus.Failed, $"HTTP {response.StatusCode}，status={JsonFields.Text(header?["status"])}：{JsonFields.Text(header?["message"])}");

        IReadOnlyList<SearchHit> hits =
        [
            .. (root?["results"] as JsonArray ?? [])
               .Select(ToHit)
               .OfType<SearchHit>()
               .OrderByDescending(hit => hit.Similarity)
        ];
        return new SearchResult
        {
            Status = SearchStatus.Ok,
            Hits   = hits,
            Detail = $"剩余额度 {JsonFields.Text(header?["short_remaining"])}/{JsonFields.Text(header?["long_remaining"])}"
        };
    }

    private static SearchHit? ToHit(JsonNode? result)
    {
        JsonNode? header = result?["header"];
        JsonNode? data = result?["data"];
        if (JsonFields.Number(header?["similarity"]) is not { } similarity || JsonFields.Number(header?["index_id"]) is not { } indexValue)
            return null;

        int index = (int)indexValue;
        bool anime = index is ANIME_INDEX or ADULT_ANIME_INDEX;
        bool adult = AdultIndexes.Contains(index);
        return new SearchHit
        {
            Engine     = SearchEngine.SauceNao,
            Similarity = similarity,
            SourceName = SourceName(JsonFields.Text(header?["index_name"])),
            Kind       = anime ? HitKind.Anime : HitKind.Illustration,
            Adult      = adult,
            Title      = anime ? JsonFields.Text(data?["source"]) : FirstText(data, "title", "jp_name", "eng_name", "material"),
            Author     = anime ? null : FirstText(data, "member_name", "author_name", "creator", "twitter_user_handle"),
            WorkId     = IdFields.Select(field => JsonFields.Text(data?[field.Field])).FirstOrDefault(id => id is not null),
            Sources    = adult || anime ? [] : Sources(data),
            Episode    = anime ? JsonFields.Text(data?["part"]) : null,
            Time       = anime ? JsonFields.Text(data?["est_time"]) : null
        };
    }

    // index_name 形如「Index #9: Danbooru - a2f2….jpg」，取中间的库名。
    private static string SourceName(string? indexName) =>
        indexName is null ? UNKNOWN_SOURCE
        : IndexName().Match(indexName) is { Success: true } match ? match.Groups["name"].Value.Trim()
        : indexName;

    private static IReadOnlyList<SourceRef> Sources(JsonNode? data)
    {
        List<SourceRef> sources = [];
        foreach ((string field, ImageSite site) in IdFields)
        {
            if (JsonFields.Text(data?[field]) is { } id)
                sources.Add(new SourceRef
                {
                    Site = site,
                    Id   = id
                });
        }

        IEnumerable<string?> links = (data?["ext_urls"] as JsonArray ?? []).Select(JsonFields.Text).Append(JsonFields.Text(data?["source"]));
        foreach (string? link in links)
        {
            if (SourceLinks.Parse(link) is not { } source) continue;
            int existing = sources.IndexOf(source);
            // pximg 链接带页号，比只有作品号的同一作品更具体。
            if (existing < 0) sources.Add(source);
            else if (source.Page is not null && sources[existing].Page is null) sources[existing] = source;
        }

        return sources;
    }

    private static string? FirstText(JsonNode? data, params string[] fields) =>
        fields.Select(field => JsonFields.Text(data?[field])).FirstOrDefault(text => text is not null);

    [GeneratedRegex(@"^Index #\d+:\s*(?<name>.+?)\s+-\s+\S+$")]
    private static partial Regex IndexName();
}
