using System.Security.Cryptography;
using Shinano.Plugin.ImageSearch;
using Shinano.Plugin.ImageSearch.Delivery;
using Shinano.Plugin.ImageSearch.Models;
using Shinano.Plugin.ImageSearch.Parsing;
using SkiaSharp;

namespace Shinano.Tests.Plugins;

/// <summary>搜图插件：以真实录制的响应验证解析，以及搜索服务结果到回复的决定与来源链接识别。</summary>
public sealed class ImageSearchTests
{
    private const string FIXTURES = "Plugins/ImageSearchFixtures";

    [Fact]
    public void SauceNao_ParsesRecordedResponse()
    {
        SearchResult result = SauceNaoParser.Parse(Upstream(200, Fixture("saucenao-127307417.json")));

        Assert.Equal(SearchStatus.Ok, result.Status);
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
    public void SauceNao_MapsLimitsAndKeyErrors()
    {
        Assert.Equal(SearchStatus.RateLimited, SauceNaoParser.Parse(Upstream(429, "{}")).Status);
        Assert.Equal(SearchStatus.Unconfigured, SauceNaoParser.Parse(Upstream(403, "{}")).Status);
        Assert.Equal(SearchStatus.Unconfigured, SauceNaoParser.Parse(new GatewayResponse { Outcome = GatewayOutcome.Unconfigured, StatusCode = 503 }).Status);
        Assert.Equal(SearchStatus.Failed, SauceNaoParser.Parse(Upstream(200, """{"header":{"status":-2,"message":"x"}}""")).Status);
        Assert.Equal(SearchStatus.Failed, SauceNaoParser.Parse(Upstream(200, "<html>challenge</html>")).Status);
    }

    [Fact]
    public void Iqdb_ParsesResultsAndTreatsQueuePageAsFailure()
    {
        SearchResult result = IqdbParser.Parse(Upstream(200, Fixture("iqdb-127307417.html")));

        Assert.Equal(SearchStatus.Ok, result.Status);
        SearchHit best = result.Hits[0];
        Assert.Equal(98, best.Similarity);
        Assert.Equal("Danbooru", best.SourceName);
        Assert.False(best.Adult);
        Assert.Equal([new SourceRef { Site = ImageSite.Danbooru, Id = "8861752" }], best.Sources);
        Assert.Contains(result.Hits, hit => hit.Sources is [{ Site: ImageSite.Yandere }]);

        Assert.Equal(SearchStatus.Failed, IqdbParser.Parse(Upstream(200, Fixture("iqdb-queue-failure.html"))).Status);
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
    public void ImageDecision_FallsBackToIqdbThenLowSimilarityThenReplies()
    {
        SearchResult strong = Ok(Hit("Danbooru", 95));
        SearchResult weak = Ok(Hit("Danbooru", 50));
        SearchResult iqdbStrong = Ok(Hit("Danbooru", 98, SearchEngine.Iqdb));

        Assert.False(SearchDecisions.NeedsIqdb(strong, 60));
        Assert.True(SearchDecisions.NeedsIqdb(weak, 60));
        Assert.Same(strong.Hits[0], SearchDecisions.ForImage(strong, null, 60, 80).Hit);
        Assert.Same(iqdbStrong.Hits[0], SearchDecisions.ForImage(weak, iqdbStrong, 60, 80).Hit);

        SearchDecision low = SearchDecisions.ForImage(weak, Ok(Hit("Danbooru", 70, SearchEngine.Iqdb)), 60, 80);
        Assert.Same(weak.Hits[0], low.Hit);
        Assert.True(low.LowSimilarity);

        SearchResult failed = SearchResult.Of(SearchStatus.Failed, "x");
        Assert.Equal(ReplyTexts.NOT_FOUND, SearchDecisions.ForImage(Ok(), failed, 60, 80).Reply);
        Assert.Equal(ReplyTexts.RATE_LIMITED, SearchDecisions.ForImage(SearchResult.Of(SearchStatus.RateLimited, "x"), failed, 60, 80).Reply);
        Assert.Equal(ReplyTexts.UNCONFIGURED, SearchDecisions.ForImage(SearchResult.Of(SearchStatus.Unconfigured, "x"), failed, 60, 80).Reply);
        Assert.Equal(ReplyTexts.SERVER_ERROR, SearchDecisions.ForImage(failed, failed, 60, 80).Reply);
        Assert.Equal(ReplyTexts.NOT_FOUND, SearchDecisions.ForImage(failed, Ok(), 60, 80).Reply);
    }

    [Fact]
    public void AnimeDecision_FallsBackToSauceNaoAnimeOnly()
    {
        SearchResult weakTrace = Ok(Anime(70));
        SearchHit sauceAnime = new()
        {
            Engine     = SearchEngine.SauceNao,
            Similarity = 92,
            SourceName = "Anime",
            Kind       = HitKind.Anime
        };
        SearchResult sauce = Ok(Hit("Danbooru", 99), sauceAnime);

        Assert.True(SearchDecisions.NeedsSauceNaoAnime(weakTrace, 90));
        Assert.Same(sauceAnime, SearchDecisions.ForAnime(weakTrace, sauce, 90, 60).Hit);
        Assert.Same(weakTrace.Hits[0], SearchDecisions.ForAnime(weakTrace, Ok(Hit("Danbooru", 99)), 90, 60).Hit);
        Assert.Equal(ReplyTexts.RATE_LIMITED, SearchDecisions.ForAnime(SearchResult.Of(SearchStatus.RateLimited, "x"), Ok(), 90, 60).Reply);
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
