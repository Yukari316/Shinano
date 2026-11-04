using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Shinano.Plugin.ImageSources.Models;

namespace Shinano.Plugin.ImageSources.Parsing;

/// <summary>
/// 解析经图源服务转发的 SauceNAO 响应（<c>output_type=2</c>）。限额用尽时上游返回 429；
/// 正文 <c>header.status</c> 为 0 表示成功，负数为请求问题，正数为服务端问题。
/// 只保留请求时限定的库的结果（见 <see cref="SearchDatabases" />）：图源服务未支持限定时上游会返回全部库的结果。
/// </summary>
public static partial class SauceNaoParser
{
    private const int    TOO_MANY_REQUESTS = 429;
    private const int    FORBIDDEN         = 403;
    private const string UNKNOWN_SOURCE    = "SauceNAO";

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

    /// <summary>解析搜索结果，只保留 <paramref name="databases" /> 中的库，按相似度从高到低排列。</summary>
    /// <param name="response">图源服务的响应。</param>
    /// <param name="databases">请求时限定的库。</param>
    public static SearchResult Parse(GatewayResponse response, IReadOnlySet<SauceNaoIndex> databases)
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
               .Select(result => ToHit(result, databases))
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

    private static SearchHit? ToHit(JsonNode? result, IReadOnlySet<SauceNaoIndex> databases)
    {
        JsonNode? header = result?["header"];
        JsonNode? data = result?["data"];
        if (JsonFields.Number(header?["similarity"]) is not { } similarity || JsonFields.Number(header?["index_id"]) is not { } indexValue)
            return null;

        SauceNaoIndex index = (SauceNaoIndex)(int)indexValue;
        if (!databases.Contains(index)) return null;

        bool anime = index is SauceNaoIndex.Anime or SauceNaoIndex.HAnime;
        return new SearchHit
        {
            Engine     = SearchEngine.SauceNao,
            Similarity = similarity,
            SourceName = SourceName(JsonFields.Text(header?["index_name"])),
            Kind       = anime ? HitKind.Anime : HitKind.Illustration,
            Adult      = index == SauceNaoIndex.HAnime,
            Title      = anime ? JsonFields.Text(data?["source"]) : FirstText(data, "title", "jp_name", "eng_name", "material"),
            Author     = anime ? null : FirstText(data, "member_name", "author_name", "creator", "twitter_user_handle"),
            WorkId     = IdFields.Select(field => JsonFields.Text(data?[field.Field])).FirstOrDefault(id => id is not null),
            Sources    = anime ? [] : Sources(data),
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
