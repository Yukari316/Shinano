using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using PuppeteerSharp;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili;
using Shinano.Plugin.Bilibili.Feed;
using Shinano.Plugin.Bilibili.Login;
using Shinano.Plugin.Bilibili.Persistence;
using Shinano.Tests.Host;
using SkiaSharp;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
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
    public void ExtraNodes_ListTruncationNoticeFlatPicturesAndVideoExtras()
    {
        Dictionary<string, BiliDynamic> items = Feed.Items.ToDictionary(item => item.Id[^1..]);
        BiliCard truncated = new()
        {
            Png        = [1],
            MoreLinkId = items["5"].Id
        };

        List<ForwardedMessageNode> opus = [.. BiliPusher.ExtraNodes(114514, "虚构研究所", items["5"], truncated)];
        Assert.Equal(5, opus.Count);
        Assert.Equal($"原文太长了喵！要坏掉了喵！\nhttps://t.bilibili.com/{items["5"].Id}", opus[0].Segments.GetText());
        Assert.All(opus[1..], node => Assert.IsType<ImageSegment>(Assert.Single(node.Segments)));
        Assert.All(opus, node => Assert.Equal("虚构研究所", node.SenderName));

        List<ForwardedMessageNode> video = [.. BiliPusher.ExtraNodes(114514, "晚风电台", items["4"], new BiliCard { Png = [1] })];
        Assert.Equal(2, video.Count);
        Assert.IsType<ImageSegment>(Assert.Single(video[0].Segments));
        Assert.Equal("BV1xx411c7aa", video[1].Segments.GetText());

        Assert.Empty(BiliPusher.ExtraNodes(114514, "虚构研究所", items["8"], null));
    }

    [Fact]
    public void DynamicsNodes_PutHeaderThenCardAndNestedExtrasPerDynamic()
    {
        Dictionary<string, BiliDynamic> items = Feed.Items.ToDictionary(item => item.Id[^1..]);

        List<ForwardedMessageNode> nodes = [.. BiliPusher.DynamicsNodes(114514, "虚构研究所", [(items["5"], new BiliCard { Png = [1] }), (items["8"], null)])];

        Assert.Equal(5, nodes.Count);
        Assert.Equal("虚构研究所更新了喵", nodes[0].Segments.GetText());
        Assert.Equal("一共2条动态喵", nodes[1].Segments.GetText());
        Assert.IsType<ImageSegment>(Assert.Single(nodes[2].Segments));
        Assert.Equal(4, Assert.IsType<ForwardSegment>(Assert.Single(nodes[3].Segments)).Messages.Count);
        // 卡片绘制失败时改发文字；没有附加内容时不加嵌套转发。
        Assert.Contains("小夜曲", nodes[4].Segments.GetText());
    }

    [Fact]
    public void TextNodes_PutHeaderThenTruncatedPlainTextPerDynamic()
    {
        BiliDynamic item = Feed.Items.Single(dynamic => dynamic.Id.EndsWith('8'));
        BiliDynamic longText = new()
        {
            Id          = "900000000000000001",
            Kind        = item.Kind,
            AuthorId    = item.AuthorId,
            AuthorName  = "虚构研究所",
            AuthorFace  = string.Empty,
            Action      = "发布了动态",
            PublishedAt = item.PublishedAt,
            Text        = [new BiliText { Kind = BiliTextKind.Plain, Text = new string('字', BiliPusher.FALLBACK_TEXT_MAX) + "😀溢出" }]
        };

        List<ForwardedMessageNode> nodes = [.. BiliPusher.TextNodes(114514, "虚构研究所", [item, longText])];

        Assert.Equal(3, nodes.Count);
        Assert.Equal("虚构研究所更新了喵", nodes[0].Segments.GetText());
        Assert.All(nodes, node => Assert.IsType<TextSegment>(Assert.Single(node.Segments)));
        string truncated = nodes[2].Segments.GetText();
        Assert.StartsWith("虚构研究所发布了动态：字", truncated);
        Assert.EndsWith("字……", truncated);
    }

    // 首选方式失败（如 IM 端下载图片失败）时改发纯文本合并转发，送达即算成功；未连接 LLBot 时不发送，返回失败以便下一轮重试。
    [Fact]
    public async Task PushDynamicsAsync_FallsBackToPlainTextForward_AndFailsWithoutConnection()
    {
        BiliDynamic item = Feed.Items.Single(dynamic => dynamic.Id.EndsWith('5'));
        (IBotApi api, ScriptedBotApi script) = ScriptedBotApi.Create(false, true);
        BiliPusher pusher = new(new RenderingContext(new BiliConfig(), new GroupBot { Api = api }), NullLogger<BiliPusher>.Instance);

        Assert.True(await pusher.PushDynamicsAsync(1000, "虚构研究所", [(item, new BiliCard { Png = [1] })]));

        Assert.Equal(2, script.Sent.Count);
        IReadOnlyList<ForwardedMessageNode> fallback = Assert.IsType<ForwardSegment>(Assert.Single(script.Sent[1])).Messages;
        Assert.Equal(2, fallback.Count);
        Assert.All(fallback, node => Assert.IsType<TextSegment>(Assert.Single(node.Segments)));

        BiliPusher offline = new(new RenderingContext(new BiliConfig(), new GroupBot()), NullLogger<BiliPusher>.Instance);
        Assert.False(await offline.PushDynamicsAsync(1000, "虚构研究所", [(item, new BiliCard { Png = [1] })]));
    }

    [Fact]
    public async Task RenderAsync_ReportsTruncatedTextByConfiguredLines()
    {
        using BiliHttp http = new(NullLogger<BiliHttp>.Instance);
        BiliConfig config = new()
        {
            OpusMaxLines    = 2,
            ForwardMaxLines = 2
        };
        BiliCardRenderer renderer = new(new RenderingContext(config), http, NullLogger<BiliCardRenderer>.Instance);
        CancellationToken token = TestContext.Current.CancellationToken;
        BiliDynamic longOpus = Dynamic("101", BiliDynamicKind.Opus, string.Join('\n', Enumerable.Repeat("一行虚构的正文", 5)));
        BiliDynamic shortOpus = Dynamic("102", BiliDynamicKind.Opus, "短");

        Assert.Equal("101", (await renderer.RenderAsync(longOpus, token)).MoreLinkId);
        Assert.Null((await renderer.RenderAsync(shortOpus, token)).MoreLinkId);
        // 专栏只有摘要，总是附加链接。
        Assert.Equal("103", (await renderer.RenderAsync(Dynamic("103", BiliDynamicKind.Article, "短"), token)).MoreLinkId);
        // 只有原动态被截断时指向原动态；两者都截断时指向转发本身。
        Assert.Equal("101", (await renderer.RenderAsync(Dynamic("104", BiliDynamicKind.Forward, "短", longOpus), token)).MoreLinkId);
        Assert.Equal("105", (await renderer.RenderAsync(Dynamic("105", BiliDynamicKind.Forward, longOpus.Text[0].Text, longOpus), token)).MoreLinkId);
        Assert.Null((await renderer.RenderAsync(Dynamic("106", BiliDynamicKind.Forward, "短", shortOpus), token)).MoreLinkId);
    }

    private static BiliDynamic Dynamic(string id, BiliDynamicKind kind, string text, BiliDynamic? original = null) =>
        new()
        {
            Id          = id,
            Kind        = kind,
            AuthorId    = 1,
            AuthorName  = "虚构UP",
            AuthorFace  = "",
            PublishedAt = DateTimeOffset.FromUnixTimeSeconds(1790000000),
            Action      = "发布了动态",
            Text        =
            [
                new BiliText
                {
                    Kind = BiliTextKind.Plain,
                    Text = text
                }
            ],
            Original = original
        };

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
        BiliCardRenderer renderer = new(new RenderingContext(new BiliConfig()), http, NullLogger<BiliCardRenderer>.Instance);
        foreach (BiliDynamic dynamic in Feed.Items)
        {
            BiliCard card = await renderer.RenderAsync(dynamic, TestContext.Current.CancellationToken);
            using SKBitmap bitmap = SKBitmap.Decode(card.Png);
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
        private readonly BiliConfig    _config;
        private readonly IBotAccessor? _bot;

        public RenderingContext(BiliConfig config, IBotAccessor? bot = null)
        {
            _config = config;
            _bot    = bot;
        }

        public string PluginId => "bilibili";

        public IPluginEvents Events => throw new NotSupportedException();

        public IPluginScheduler Scheduler => throw new NotSupportedException();

        public IPluginDatabase Database => throw new NotSupportedException();

        public IPluginMedia Media => throw new NotSupportedException();

        public IPluginBuckets Buckets => throw new NotSupportedException();

        public IBotAccessor Bot => _bot ?? throw new NotSupportedException();

        public IPluginAssets Assets => throw new NotSupportedException();

        public IPluginPanel Panel => throw new NotSupportedException();

        public IPluginFonts Fonts => TestRendering.Fonts;

        public IPluginImages Images => TestRendering.Images;

        public IPluginBrowser Browser => throw new NotSupportedException();

        public IPluginTools Tools => throw new NotSupportedException();

        public string DataDirectory => throw new NotSupportedException();

        public void OnEnabledChanged(Action<bool> handler) => throw new NotSupportedException();

        public IPluginNotices Notices => throw new NotSupportedException();

        public IPluginConfig<T> Config<T>() where T : class, new() =>
            new FixedConfig<T>
            {
                // SAFE: 绘制器只读取 BiliConfig。
                Value = (T)(object)_config
            };

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }

    private sealed class FixedConfig<T> : IPluginConfig<T> where T : class
    {
        public required T Value { get; init; }
    }

    private sealed class GroupBot : IBotAccessor
    {
        public IBotApi? Api { get; init; }

        public long SelfId => 10001;

        public IReadOnlyList<long> SuperUsers => [];

        public Task<MilkyCallResult> CallMilkyAsync(string action, JsonObject parameters, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>按顺序给出群消息的发送结果并记录发出的消息；其他调用不支持。</summary>
    public class ScriptedBotApi : DispatchProxy
    {
        private readonly Queue<bool> _results = [];

        public List<MessageBody> Sent { get; } = [];

        public static (IBotApi Api, ScriptedBotApi Script) Create(params bool[] results)
        {
            IBotApi api = Create<IBotApi, ScriptedBotApi>();
            ScriptedBotApi script = (ScriptedBotApi)(object)api;
            foreach (bool result in results) script._results.Enqueue(result);
            return (api, script);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            // SAFE: DispatchProxy 调用接口方法时总会给出方法与参数。
            if (targetMethod!.Name != nameof(IBotApi.SendGroupMessageAsync)) throw new NotSupportedException(targetMethod.Name);
            Sent.Add((MessageBody)args![1]!);
            return ValueTask.FromResult(_results.Dequeue() ? SendMessageResult.Ok(1) : SendMessageResult.Fail(ApiStatusCode.ProtocolError, "下载图片失败"));
        }
    }
}
