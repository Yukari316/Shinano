using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.ImageSearch;
using Shinano.Plugin.ImageSearch.Delivery;
using Shinano.Plugin.ImageSearch.Models;
using Shinano.Plugin.ImageSearch.Parsing;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Models;
using Shinano.Plugin.ImageSources.Parsing;
using SkiaSharp;

namespace Shinano.Tests.Plugins;

/// <summary>搜图插件：以真实录制的响应验证解析，以及搜索服务结果到回复的决定与来源链接识别。</summary>
public sealed class ImageSearchTests
{
    private const string FIXTURES = "Plugins/ImageSearchFixtures";

    [Fact]
    public void SauceNao_ParsesRecordedResponse()
    {
        SearchResult result = SauceNaoParser.Parse(Upstream(200, Fixture("saucenao-127307417.json")), SearchDatabases.SauceNaoIllustrations);

        Assert.Equal(SearchStatus.Ok, result.Status);
        // 录制的响应查了全部库：Twitter 与 Nijie 的结果不在插画图库中，被丢弃。
        Assert.Equal(["Danbooru", "Pixiv Images"], result.Hits.Select(hit => hit.SourceName));
        SearchHit best = result.Hits[0];
        Assert.Equal(95.39, best.Similarity);
        Assert.Equal("Danbooru", best.SourceName);
        Assert.Equal("uriuritei", best.Author);
        Assert.Equal("8861752", best.WorkId);
        Assert.Contains(new SourceRef { Site = ImageSite.Danbooru, Id = "8861752" }, best.Sources);
        Assert.Contains(best.Sources, source => source.Site == ImageSite.Gelbooru);
        Assert.True(result.Hits.Zip(result.Hits.Skip(1)).All(pair => pair.First.Similarity >= pair.Second.Similarity));
        Assert.Same(best, SearchDecisions.ForImage(result, null, 60, 80).Hit);
    }

    [Fact]
    public void SauceNao_AnimeDatabasesKeepOnlyAnimeAndMarkHAnimeAdult()
    {
        const string BODY = """
            {"header":{"status":0},"results":[
              {"header":{"similarity":"97.1","index_id":9,"index_name":"Index #9: Danbooru - a.jpg"},"data":{"danbooru_id":1}},
              {"header":{"similarity":"91.5","index_id":22,"index_name":"Index #22: H-Anime - b.jpg"},"data":{"source":"H","part":"2","est_time":"00:03:01"}},
              {"header":{"similarity":"88.0","index_id":21,"index_name":"Index #21: Anime - c.jpg"},"data":{"source":"A","part":"5","est_time":"00:12:00"}}
            ]}
            """;

        SearchResult result = SauceNaoParser.Parse(Upstream(200, BODY), SearchDatabases.SauceNaoAnime);

        Assert.Equal([("H", true), ("A", false)], result.Hits.Select(hit => (hit.Title, hit.Adult)));
        Assert.All(result.Hits, hit => Assert.Equal(HitKind.Anime, hit.Kind));
        Assert.Empty(SauceNaoParser.Parse(Upstream(200, Fixture("saucenao-127307417.json")), SearchDatabases.SauceNaoAnime).Hits);
    }

    [Fact]
    public void SauceNao_MapsLimitsAndKeyErrors()
    {
        IReadOnlySet<SauceNaoIndex> databases = SearchDatabases.SauceNaoIllustrations;
        Assert.Equal(SearchStatus.RateLimited, SauceNaoParser.Parse(Upstream(429, "{}"), databases).Status);
        Assert.Equal(SearchStatus.Unconfigured, SauceNaoParser.Parse(Upstream(403, "{}"), databases).Status);
        Assert.Equal(SearchStatus.Unconfigured, SauceNaoParser.Parse(new GatewayResponse { Outcome = GatewayOutcome.Unconfigured, StatusCode = 503 }, databases).Status);
        Assert.Equal(SearchStatus.Failed, SauceNaoParser.Parse(Upstream(200, """{"header":{"status":-2,"message":"x"}}"""), databases).Status);
        Assert.Equal(SearchStatus.Failed, SauceNaoParser.Parse(Upstream(200, "<html>challenge</html>"), databases).Status);
    }

    [Fact]
    public void Iqdb_ParsesResultsAndTreatsQueuePageAsFailure()
    {
        SearchResult result = IqdbParser.Parse(Upstream(200, Fixture("iqdb-127307417.html")), SearchDatabases.IqdbIllustrations);

        Assert.Equal(SearchStatus.Ok, result.Status);
        SearchHit best = result.Hits[0];
        Assert.Equal(98, best.Similarity);
        Assert.Equal("Danbooru", best.SourceName);
        Assert.False(best.Adult);
        Assert.Equal([new SourceRef { Site = ImageSite.Danbooru, Id = "8861752" }], best.Sources);
        Assert.Contains(result.Hits, hit => hit.Sources is [{ Site: ImageSite.Yandere }]);
        Assert.All(result.Hits, hit => Assert.Contains(Assert.Single(hit.Sources).Site, SearchDatabases.IqdbIllustrations));

        // 只保留限定的图站：不含 Danbooru 时最佳结果换成其他图站。
        SearchResult yandereOnly = IqdbParser.Parse(Upstream(200, Fixture("iqdb-127307417.html")), new HashSet<ImageSite> { ImageSite.Yandere });
        Assert.All(yandereOnly.Hits, hit => Assert.Equal(ImageSite.Yandere, Assert.Single(hit.Sources).Site));
        Assert.NotEmpty(yandereOnly.Hits);

        Assert.Equal(SearchStatus.Failed, IqdbParser.Parse(Upstream(200, Fixture("iqdb-queue-failure.html")), SearchDatabases.IqdbIllustrations).Status);
    }

    [Fact]
    public void TraceMoe_ParsesRecordedResponse()
    {
        SearchResult result = TraceMoeParser.Parse(Upstream(200, Fixture("tracemoe-127307417.json")));

        Assert.Equal(SearchStatus.Ok, result.Status);
        SearchHit best = result.Hits[0];
        Assert.Equal(HitKind.Anime, best.Kind);
        Assert.Equal("輝夜姬想讓人告白~天才們的戀愛頭腦戰~", best.Title);
        Assert.Equal("4", best.Episode);
        Assert.Equal("13:06", best.Time);
        Assert.InRange(best.Similarity, 67, 68);
        Assert.False(best.Adult);
        Assert.StartsWith("https://api.trace.moe/image/", best.PreviewUrl);
        Assert.Equal(SearchStatus.RateLimited, TraceMoeParser.Parse(Upstream(402, "{}")).Status);
        Assert.Equal("1:02:03", TraceMoeParser.FormatTime(3723.9));
    }

    [Theory]
    [InlineData("https://i.pximg.net/img-original/img/2025/02/15/00/00/00/127307417_p2.png", ImageSite.Pixiv, "127307417", 2)]
    [InlineData("https://www.pixiv.net/en/artworks/151487208", ImageSite.Pixiv, "151487208", null)]
    [InlineData("https://www.pixiv.net/member_illust.php?mode=medium&illust_id=31470951", ImageSite.Pixiv, "31470951", null)]
    [InlineData("https://danbooru.donmai.us/post/show/8861752", ImageSite.Danbooru, "8861752", null)]
    [InlineData("https://gelbooru.com/index.php?page=post&s=view&id=11463176", ImageSite.Gelbooru, "11463176", null)]
    [InlineData("https://yande.re/post/show/995710", ImageSite.Yandere, "995710", null)]
    [InlineData("https://konachan.com/post/show/383964", ImageSite.Konachan, "383964", null)]
    [InlineData("https://chan.sankakucomplex.com/posts/8yrxQ0jxkRE", ImageSite.Sankaku, "8yrxQ0jxkRE", null)]
    [InlineData("https://e-shuushuu.net/image/1122217/", ImageSite.Eshuushuu, "1122217", null)]
    public void SourceLinks_RecognizesWorkPages(string url, ImageSite site, string id, int? page)
    {
        SourceRef? source = SourceLinks.Parse(url);

        Assert.NotNull(source);
        Assert.Equal((site, id, page), (source.Site, source.Id, source.Page));
    }

    [Fact]
    public void SourceLinks_IgnoresOtherLinks()
    {
        Assert.Null(SourceLinks.Parse("https://twitter.com/uriuritei/status/1890725357273161985"));
        Assert.Null(SourceLinks.Parse("https://gelbooru.com/index.php?page=post&s=list&md5=a2f243e7ff4114100ed88fb7f6732592"));
        Assert.Null(SourceLinks.Parse(null));
    }

    [Fact]
    public void ImageDecision_SendsBestHitAndFallsBackOnlyWhenUnavailable()
    {
        SearchResult strong = Ok(Hit("Danbooru", 95));
        SearchResult weak = Ok(Hit("Danbooru", 50));
        SearchResult failed = SearchResult.Of(SearchStatus.Failed, "x");
        SearchResult iqdb = Ok(Hit("Danbooru", 70, SearchEngine.Iqdb));

        Assert.False(SearchDecisions.NeedsFallback(weak));
        Assert.True(SearchDecisions.NeedsFallback(failed));
        Assert.True(SearchDecisions.NeedsFallback(SearchResult.Of(SearchStatus.RateLimited, "x")));
        Assert.False(SearchDecisions.ForImage(strong, null, 60, 80).LowSimilarity);

        // 主服务完成搜索时低于阈值也回复其最高结果，只标为低相似度。
        SearchDecision low = SearchDecisions.ForImage(weak, null, 60, 80);
        Assert.Same(weak.Hits[0], low.Hit);
        Assert.True(low.LowSimilarity);
        Assert.Equal(60, low.Threshold);

        SearchDecision fallback = SearchDecisions.ForImage(failed, iqdb, 60, 80);
        Assert.Same(iqdb.Hits[0], fallback.Hit);
        Assert.True(fallback.LowSimilarity);
        Assert.Equal(80, fallback.Threshold);

        Assert.Equal(ReplyTexts.NOT_FOUND, SearchDecisions.ForImage(Ok(), null, 60, 80).Reply);
        Assert.Equal(ReplyTexts.NOT_FOUND, SearchDecisions.ForImage(failed, Ok(), 60, 80).Reply);
        Assert.Equal(ReplyTexts.RATE_LIMITED, SearchDecisions.ForImage(SearchResult.Of(SearchStatus.RateLimited, "x"), failed, 60, 80).Reply);
        Assert.Equal(ReplyTexts.UNCONFIGURED, SearchDecisions.ForImage(SearchResult.Of(SearchStatus.Unconfigured, "x"), failed, 60, 80).Reply);
        Assert.Equal(ReplyTexts.SERVER_ERROR, SearchDecisions.ForImage(failed, failed, 60, 80).Reply);
    }

    [Fact]
    public void AnimeDecision_FallsBackToSauceNaoAnimeOnlyWhenUnavailable()
    {
        SearchResult weakTrace = Ok(Anime(70));
        SearchHit sauceAnime = new()
        {
            Engine     = SearchEngine.SauceNao,
            Similarity = 92,
            SourceName = "Anime",
            Kind       = HitKind.Anime
        };
        SearchResult failed = SearchResult.Of(SearchStatus.Failed, "x");

        Assert.Same(weakTrace.Hits[0], SearchDecisions.ForAnime(weakTrace, null, 90, 60).Hit);
        // 兜底只查 SauceNAO 的番剧库，直接取其最高结果。
        Assert.Same(sauceAnime, SearchDecisions.ForAnime(failed, Ok(sauceAnime), 90, 60).Hit);
        Assert.Equal(ReplyTexts.NOT_FOUND, SearchDecisions.ForAnime(failed, Ok(), 90, 60).Reply);
        Assert.Equal(ReplyTexts.RATE_LIMITED, SearchDecisions.ForAnime(SearchResult.Of(SearchStatus.RateLimited, "x"), failed, 90, 60).Reply);
    }

    [Fact]
    public void WorkTags_ReadPixivAndBooruShapes()
    {
        JsonObject illust = JsonNode.Parse("""
            {"tags":[{"name":"オリジナル","translated_name":"原创"},{"name":"R-18","translated_name":null},{"name":"オリジナル","translated_name":"原创"}]}
            """)!.AsObject();
        Assert.Equal([("オリジナル", "原创"), ("R-18", null)], WorkTags.FromPixiv(illust).Select(tag => (tag.Name, tag.Translation)));

        Assert.Equal(["1girl", "long_hair"], Names("""{"tag_string":"1girl long_hair  1girl","tags":"ignored"}"""));
        Assert.Equal(["1girl", "solo"], Names("""{"tags":"1girl solo"}"""));
        Assert.Equal(["blue eyes", "cat"], Names("""{"tags":[{"name":"blue eyes"},{"title":"cat"}]}"""));
        Assert.Empty(Names("{}"));

        static IEnumerable<string> Names(string post) => WorkTags.FromPost(JsonNode.Parse(post)!.AsObject()).Select(tag => tag.Name);
    }

    [Fact]
    public void IllustrationCard_PrefersPixivWorkAndLimitsBooruTags()
    {
        SearchHit hit = new()
        {
            Engine     = SearchEngine.SauceNao,
            Similarity = 95,
            SourceName = "Danbooru",
            Kind       = HitKind.Illustration,
            Title      = "booru title",
            Author     = "booru_artist",
            WorkId     = "8861752"
        };
        ImageResolution pixiv = new()
        {
            Work   = new SourceRef { Site = ImageSite.Pixiv, Id = "127307417", Page = 0 },
            Title  = "夏の終わり",
            Author = "再音さく",
            Tags   = [new WorkTag { Name = "オリジナル", Translation = "原创" }]
        };
        ImageResolution booru = new()
        {
            Work = new SourceRef { Site = ImageSite.Danbooru, Id = "8861752" },
            Tags = [.. Enumerable.Range(1, 30).Select(index => new WorkTag { Name = $"tag_{index}" })]
        };

        // 查到 Pixiv：来源名、标题、作者、作品号都换成 Pixiv 的信息，标签原值不翻译。
        InfoCard fromPixiv = SearchCards.Illustration(hit, pixiv, 60, false, 20);
        Assert.Equal("Pixiv", fromPixiv.Title);
        Assert.Equal(["标题：夏の終わり", "作者：再音さく", "作品号：127307417"], fromPixiv.Rows.SkipLast(1).Select(row => $"{row.Label}：{row.Text}"));
        Assert.Equal(["オリジナル"], fromPixiv.Rows[^1].Items);

        // 图站：用搜索结果的信息，标签按上限截取。
        InfoCard fromBooru = SearchCards.Illustration(hit, booru, 60, false, 20);
        Assert.Equal("Danbooru", fromBooru.Title);
        Assert.Equal("booru title", fromBooru.Rows[0].Text);
        CardRow tags = fromBooru.Rows[^1];
        Assert.Equal((CardRowStyle.Items, true, 20), (tags.Style, tags.ItemBorder, tags.Items.Count));

        // 都查不到或上限为 0 时没有 TAG 行。
        Assert.DoesNotContain(SearchCards.Illustration(hit, ImageResolution.None, 60, false, 20).Rows, row => row.Style == CardRowStyle.Items);
        Assert.DoesNotContain(SearchCards.Illustration(hit, booru, 60, false, 0).Rows, row => row.Style == CardRowStyle.Items);
    }

    [Fact]
    public void PixelTweak_ChangesContentButKeepsImage()
    {
        using SKBitmap bitmap = new(8, 6);
        bitmap.Erase(SKColors.Teal);
        using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        byte[] original = encoded.ToArray();

        byte[]? tweaked = PixelTweak.Apply(original, "image/png");

        Assert.NotNull(tweaked);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(original)), Convert.ToHexString(SHA256.HashData(tweaked)));
        using SKBitmap decoded = SKBitmap.Decode(tweaked);
        Assert.Equal((8, 6), (decoded.Width, decoded.Height));
        Assert.Null(PixelTweak.Apply(original, "image/gif"));
        Assert.Null(PixelTweak.Apply([1, 2, 3], "image/jpeg"));
    }

    [Fact]
    public void AdultPlaceholder_IsEmbeddedJpeg()
    {
        const string BASE64_PREFIX = "base64://";

        Assert.StartsWith(BASE64_PREFIX, AdultPlaceholder.ImageUri);
        using SKCodec? codec = SKCodec.Create(new SKMemoryStream(Convert.FromBase64String(AdultPlaceholder.ImageUri[BASE64_PREFIX.Length..])));
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec?.EncodedFormat);
    }

    [Fact]
    public void ImageInspector_FormatsSizeWithDynamicUnit()
    {
        Assert.Equal("512 B", ImageInspector.FormatSize(512));
        Assert.Equal("1.6 KB", ImageInspector.FormatSize(1638));
        Assert.Equal("8.7 MB", ImageInspector.FormatSize(9123456));
        Assert.Equal("未知", ImageInspector.FormatSize(null));
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, FIXTURES, name));

    private static GatewayResponse Upstream(int status, string body) =>
        new()
        {
            Outcome    = GatewayOutcome.Upstream,
            StatusCode = status,
            Body       = body
        };

    private static SearchResult Ok(params SearchHit[] hits) =>
        new()
        {
            Status = SearchStatus.Ok,
            Hits   = [.. hits.OrderByDescending(hit => hit.Similarity)]
        };

    private static SearchHit Hit(string source, double similarity, SearchEngine engine = SearchEngine.SauceNao) =>
        new()
        {
            Engine     = engine,
            Similarity = similarity,
            SourceName = source,
            Kind       = HitKind.Illustration
        };

    private static SearchHit Anime(double similarity) =>
        new()
        {
            Engine     = SearchEngine.TraceMoe,
            Similarity = similarity,
            SourceName = "trace.moe",
            Kind       = HitKind.Anime
        };
}
