using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using PuppeteerSharp;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili;
using Shinano.Plugin.Bilibili.Entities;
using Shinano.Plugin.Bilibili.Models;
using Shinano.Tests.Rendering;
using SkiaSharp;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Tests.Plugins;

public sealed class BilibiliTests
{
    private static readonly BiliFeedPage Feed = LoadFeed();

    [Fact]
    public void ParsePage_MapsKindsTextAndMedia()
    {
        Assert.Equal("1100000000000000001", Feed.NextOffset);
        Assert.Equal("1100000000000000008", Feed.UpdateBaseline);
        Dictionary<string, BiliDynamic> items = Feed.Items.ToDictionary(item => item.Id[^1..]);

        Assert.True(items["1"].Pinned);
        BiliDynamic opus = items["5"];
        Assert.Equal(BiliDynamicKind.Opus, opus.Kind);
        Assert.Equal("冬季企划", opus.Title);
        Assert.Equal("发布了动态", opus.Action);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790000300), opus.PublishedAt);
        Assert.Equal(
            [BiliTextKind.Plain, BiliTextKind.Highlight, BiliTextKind.Plain, BiliTextKind.Highlight, BiliTextKind.Emoji],
            opus.Text.Select(text => text.Kind));
        Assert.Equal(4, opus.Pictures.Count);
        Assert.All(opus.Pictures, picture => Assert.StartsWith("https://", picture.Url));
        Assert.Equal("预约：新曲首播", opus.Attachment);

        BiliDynamic video = items["4"];
        Assert.Equal(BiliDynamicKind.Video, video.Kind);
        Assert.Equal("BV1xx411c7aa", video.Video?.Bvid);
        Assert.Equal("04:32", video.Video?.Duration);

        BiliDynamic forward = items["3"];
        Assert.Equal(BiliDynamicKind.Forward, forward.Kind);
        Assert.Equal("转发了动态", forward.Action);
        Assert.Equal("https://img.invalid/archive/8.jpg", forward.Original?.Video?.Cover);
        Assert.Null(items["7"].Original);

        Assert.Equal(BiliDynamicKind.Article, items["2"].Kind);
        Assert.Equal(BiliDynamicKind.LiveRecommend, items["6"].Kind);
        Assert.Equal(BiliDynamicKind.Generic, items["8"].Kind);
        Assert.Equal("小夜曲", items["8"].Title);
    }

    [Fact]
    public void NewSince_SkipsPinnedAndLiveRecommendAndKeepsLatestThreeInOrder()
    {
        IReadOnlyList<BiliDynamic> pending = BiliRules.NewSince(Feed.Items, 1780000000, "1100000000000000001", out int skipped);

        // 6 条在进度之后的可推送动态（置顶与直播推荐除外），只保留最新 3 条，按发布时间先后。
        Assert.Equal(3, skipped);
        Assert.Equal(["1100000000000000005", "1100000000000000007", "1100000000000000008"], pending.Select(item => item.Id));
    }

    [Fact]
    public void NewSince_UsesIdWithinTheSameSecond()
    {
        IReadOnlyList<BiliDynamic> pending = BiliRules.NewSince(Feed.Items.Where(item => item.PublishedAt.ToUnixTimeSeconds() == 1790000200), 1790000200, "1100000000000000003", out _);

        Assert.Equal(["1100000000000000004"], pending.Select(item => item.Id));
        Assert.Equal("1100000000000000008", BiliRules.Latest(Feed.Items)?.Id);
    }

    [Fact]
    public void Change_DetectsStartAndEnd()
    {
        BiliLiveStatus live = new() { Uid = 1, RoomId = 2, Live = true };
        BiliLiveStatus offline = new() { Uid = 1, RoomId = 2, Live = false };

        Assert.Equal(BiliLiveChange.Started, BiliRules.Change(null, live));
        Assert.Equal(BiliLiveChange.None, BiliRules.Change(null, offline));
        Assert.Equal(BiliLiveChange.Ended, BiliRules.Change(new BiliLiveRecord { Uid = 1, Live = true }, offline));
        Assert.Equal(BiliLiveChange.None, BiliRules.Change(new BiliLiveRecord { Uid = 1, Live = true }, live));
    }

    [Theory]
    [InlineData(30, "不到1分钟")]
    [InlineData(59 * 60, "59分钟")]
    [InlineData(2 * 3600 + 13 * 60 + 5, "2小时13分钟")]
    [InlineData(26 * 3600, "26小时0分钟")]
    public void Duration_FormatsHoursAndMinutes(int seconds, string expected) =>
        Assert.Equal(expected, ReplyTexts.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void DynamicNodes_SendsCardPicturesAndVideoExtras()
    {
        Dictionary<string, BiliDynamic> items = Feed.Items.ToDictionary(item => item.Id[^1..]);

        List<ForwardedMessageNode> opus = [.. BiliPusher.DynamicNodes(114514, "虚构研究所", items["5"], [1, 2, 3])];
        Assert.Equal(2, opus.Count);
        Assert.IsType<ImageSegment>(Assert.Single(opus[0].Segments));
        ForwardSegment nested = Assert.IsType<ForwardSegment>(Assert.Single(opus[1].Segments));
        Assert.Equal(4, nested.Messages.Count);
        Assert.All(opus, node => Assert.Equal("虚构研究所", node.SenderName));

        List<ForwardedMessageNode> article = [.. BiliPusher.DynamicNodes(114514, "虚构研究所", items["2"], null)];
        Assert.Equal(2, article.Count);
        Assert.Contains("专栏标题", article[0].Segments.GetText());
        Assert.IsType<ImageSegment>(Assert.Single(article[1].Segments));

        List<ForwardedMessageNode> video = [.. BiliPusher.DynamicNodes(114514, "晚风电台", items["4"], [1])];
        Assert.Equal(3, video.Count);
        Assert.Equal("BV1xx411c7aa", video[2].Segments.GetText());
    }

    // .NET 解析 Domain=bilibili.com 后不带前导点，写入浏览器时必须补上，否则子域收不到登录 Cookie。
    [Fact]
    public void ToBrowserCookie_MakesDomainCookieForAllSubdomains()
    {
        CookieContainer container = new();
        Uri uri = new("https://passport.bilibili.com/x/passport-login/web/qrcode/poll");
        container.SetCookies(uri, "SESSDATA=fake%2C1%2Cvalue; Path=/; Domain=bilibili.com; Expires=Mon, 08 May 2027 07:53:14 GMT; HttpOnly; Secure");

        CookieParam cookie = BiliSession.ToBrowserCookie(container.GetAllCookies()[0]);

        Assert.Equal(".bilibili.com", cookie.Domain);
        Assert.Equal("fake%2C1%2Cvalue", cookie.Value);
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(new DateTimeOffset(2027, 5, 8, 7, 53, 14, TimeSpan.Zero).ToUnixTimeSeconds(), cookie.Expires);
    }

    // 样本中的图片地址指向 .invalid 域名，下载失败时画占位块；验证每种动态都能绘制且宽度为配置的输出宽度。
    [Fact]
    public async Task RenderAsync_DrawsEveryKindWithPlaceholders()
    {
        using BiliHttp http = new(NullLogger<BiliHttp>.Instance);
        BiliCardRenderer renderer = new(new RenderingContext(), http, NullLogger<BiliCardRenderer>.Instance);
        foreach (BiliDynamic dynamic in Feed.Items)
        {
            byte[] png = await renderer.RenderAsync(dynamic, TestContext.Current.CancellationToken);
            using SKBitmap bitmap = SKBitmap.Decode(png);
            Assert.Equal(TestRendering.WIDTH, bitmap.Width);
            Assert.True(bitmap.Height > bitmap.Width / 3);
        }
    }

    private static BiliFeedPage LoadFeed()
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Plugins", "BilibiliFixtures", "feed.json")));
        return BiliDynamicParser.ParsePage(document.RootElement.GetProperty("data"));
    }

    private sealed class RenderingContext : IPluginInitializationContext
    {
        public string PluginId => "bilibili";

        public IPluginEvents Events => throw new NotSupportedException();

        public IPluginScheduler Scheduler => throw new NotSupportedException();

        public IPluginDatabase Database => throw new NotSupportedException();

        public IPluginMedia Media => throw new NotSupportedException();

        public IPluginBuckets Buckets => throw new NotSupportedException();

        public IBotAccessor Bot => throw new NotSupportedException();

        public IPluginAssets Assets => throw new NotSupportedException();

        public IPluginPanel Panel => throw new NotSupportedException();

        public IPluginFonts Fonts => TestRendering.Fonts;

        public IPluginImages Images => TestRendering.Images;

        public IPluginBrowser Browser => throw new NotSupportedException();

        public IPluginNotices Notices => throw new NotSupportedException();

        public IPluginConfig<T> Config<T>() where T : class, new() => throw new NotSupportedException();

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }
}
