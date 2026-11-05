using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Shinano.Plugin.ImageSearch.Models;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Models;
using Shinano.Plugin.ImageSources.Parsing;

namespace Shinano.Plugin.ImageSearch.Parsing;

/// <summary>
/// 解析经图源服务转发的 IQDB 结果页（HTML）。每条结果是一个 <c>&lt;table&gt;</c>，含作品链接、站点名、
/// 尺寸与分级标注（Safe/Ero/Explicit）和「NN% similarity」。高负载时页面只有排队提示与读取失败，没有结果表格。
/// 只保留作品链接属于请求时限定图站的结果（见 <see cref="SearchDatabases.IqdbIllustrations" />）。
/// </summary>
public static partial class IqdbParser
{
    private const string YOUR_IMAGE   = "Your image";
    private const string NO_MATCHES   = "No relevant matches";
    private const string READ_FAILURE = "Can't read query result";
    private const string EXPLICIT     = "Explicit";
    private const string IQDB_NAME    = "IQDB";
    private const int    DETAIL_CHARS = 200;

    /// <summary>
    /// 解析结果页，只保留 <paramref name="sites" /> 的结果，按相似度从高到低排列；没有结果表格且不是「无匹配」页时视为失败，
    /// 有结果表格但都不属于这些图站时为没有结果。
    /// </summary>
    /// <param name="response">图源服务的响应。</param>
    /// <param name="sites">请求时限定的图站。</param>
    public static SearchResult Parse(GatewayResponse response, IReadOnlySet<ImageSite> sites)
    {
        switch (response.Outcome)
        {
            case GatewayOutcome.Unauthorized or GatewayOutcome.Unconfigured:
                return SearchResult.Of(SearchStatus.Unconfigured, response.Body);
            case GatewayOutcome.Failed:
                return SearchResult.Of(SearchStatus.Failed, response.Body);
        }

        if (response.StatusCode != (int)HttpStatusCode.OK) return SearchResult.Of(SearchStatus.Failed, $"HTTP {response.StatusCode}");

        List<SearchHit> hits = [];
        foreach (Match table in Table().Matches(response.Body))
        {
            string content = table.Groups["content"].Value;
            if (content.Contains(YOUR_IMAGE, StringComparison.Ordinal)) continue;
            if (ToHit(content) is { } hit) hits.Add(hit);
        }

        if (hits.Count > 0)
            return new SearchResult
            {
                Status = SearchStatus.Ok,
                Hits   = [.. hits.Where(hit => hit.Sources is [{ } source] && sites.Contains(source.Site)).OrderByDescending(hit => hit.Similarity)]
            };

        return response.Body.Contains(NO_MATCHES, StringComparison.Ordinal)
            ? new SearchResult { Status = SearchStatus.Ok }
            : SearchResult.Of(SearchStatus.Failed, Summary(response.Body));
    }

    private static SearchHit? ToHit(string table)
    {
        if (Link().Match(table) is not { Success: true } link
         || Similarity().Match(table) is not { Success: true } similarity)
            return null;

        string url = WebUtility.HtmlDecode(link.Groups["url"].Value);
        if (url.StartsWith("//", StringComparison.Ordinal)) url = "https:" + url;
        SourceRef? source = SourceLinks.Parse(url);
        string rating = Rating().Match(table).Groups["rating"].Value;
        return new SearchHit
        {
            Engine     = SearchEngine.Iqdb,
            Similarity = double.Parse(similarity.Groups["value"].Value, CultureInfo.InvariantCulture),
            SourceName = Service().Match(table) is { Success: true } service ? service.Groups["name"].Value.Trim() : IQDB_NAME,
            Kind       = HitKind.Illustration,
            Adult      = rating == EXPLICIT,
            WorkId     = source?.Id,
            Sources    = source is null ? [] : [source]
        };
    }

    /// <summary>
    /// IQDB 排队后返回的读取失败页（HTTP 200，正文为「Can't read query result!」）。这是上游的间歇性问题，
    /// 不计额度，重试通常能成功（见图源服务 docs/search-engines.md）。
    /// </summary>
    public static bool IsReadFailure(GatewayResponse response) =>
        response is { Outcome: GatewayOutcome.Upstream, StatusCode: (int)HttpStatusCode.OK } && response.Body.Contains(READ_FAILURE, StringComparison.Ordinal);

    // 失败页只保留可读文字的开头，用于日志。
    private static string Summary(string html) =>
        string.Concat(Whitespace().Replace(Tag().Replace(html, " "), " ").Trim().Take(DETAIL_CHARS));

    [GeneratedRegex(@"<table>(?<content>.*?)</table>", RegexOptions.Singleline)]
    private static partial Regex Table();

    [GeneratedRegex(@"<td class='image'><a href=""(?<url>[^""]+)""")]
    private static partial Regex Link();

    [GeneratedRegex(@"(?<value>\d+(?:\.\d+)?)% similarity")]
    private static partial Regex Similarity();

    [GeneratedRegex(@"\[(?<rating>[A-Za-z]+)\]")]
    private static partial Regex Rating();

    [GeneratedRegex(@"class=""service-icon"">(?<name>[^<]+)")]
    private static partial Regex Service();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
