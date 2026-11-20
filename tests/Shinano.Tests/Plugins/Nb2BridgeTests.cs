using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Framework.Plugins;
using Shinano.Plugin.NB2Bridge.Milky;
using Shinano.Plugin.NB2Bridge.PluginPackages;
using Shinano.Plugin.NB2Bridge.Runtime;
using Shinano.Tool.Setup;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Tests.Plugins;

/// <summary>
/// NB2Bridge：消息段与事件的 Milky 转换、<c>.env</c> 写入、插件清单与工具的契约，以及用 C# 客户端扮演 NB2 连接 Milky 实现端
/// （鉴权、事件交付、处理结果回报与超时、发送转 Sora、其他动作转发），以及进程守护的自动重启（以立即退出的系统程序代替 NB2）。
/// NB2 进程本身由开发部署实测。
/// </summary>
public sealed class Nb2BridgeTests
{
    private const long SELF_ID  = 10001;
    private const long GROUP_ID = 123456;
    private const long SENDER   = 654321;
    private const string TOKEN  = "test-token";

    [Fact]
    public void ToMilky_KeepsOnlyMilkyIncomingFields()
    {
        MessageBody body = new MessageBody("看看这个").AddMention(SENDER).AddReply(42);
        JsonArray segments = MilkySegments.ToMilky(body);

        Assert.Equal(["text", "mention", "reply"], segments.Select(segment => segment!["type"]!.GetValue<string>()));
        Assert.Equal(SENDER, segments[1]!["data"]!["user_id"]!.GetValue<long>());
        // 引用未内嵌正文时只有消息序号，适配器据此解析为出站形式的引用。
        Assert.Equal(["message_seq"], segments[2]!["data"]!.AsObject().Select(pair => pair.Key));
    }

    [Fact]
    public void FromMilky_ConvertsNestedForwardAndDropsUnknownTypes()
    {
        JsonArray segments = JsonNode.Parse(
            """
            [
              { "type": "text", "data": { "text": "解析结果" } },
              { "type": "image", "data": { "uri": "https://example.invalid/a.png", "sub_type": "sticker", "summary": null } },
              { "type": "forward", "data": { "messages": [
                  { "user_id": 1, "sender_name": "解析", "time": null, "segments": [ { "type": "video", "data": { "uri": "https://example.invalid/v.mp4", "thumb_uri": null } } ] }
              ] } },
              { "type": "file", "data": { "file_id": "x" } }
            ]
            """)!.AsArray();
        List<string> dropped = [];

        MessageBody body = MilkySegments.FromMilky(segments, dropped);

        Assert.Equal(["file"], dropped);
        Assert.Equal("解析结果", Assert.IsType<TextSegment>(body[0]).Text);
        ImageSegment image = Assert.IsType<ImageSegment>(body[1]);
        Assert.Equal(ImageSubType.Sticker, image.SubType);
        ForwardedMessageNode node = Assert.Single(Assert.IsType<ForwardSegment>(body[2]).Messages);
        Assert.Equal("https://example.invalid/v.mp4", Assert.IsType<VideoSegment>(Assert.Single(node.Segments)).FileUri);
    }

    [Fact]
    public async Task RewriteSegments_UploadsOnlyMediaUris()
    {
        string file = new Uri(CreateTempFile()).AbsoluteUri;
        JsonArray segments = new()
        {
            Segment("text", new JsonObject { ["text"] = file }),
            Segment("image", new JsonObject { ["uri"] = file, ["summary"] = file }),
            Segment(
                "forward",
                new JsonObject
                {
                    ["messages"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["segments"] = new JsonArray { Segment("video", new JsonObject { ["uri"] = file, ["thumb_uri"] = file }) }
                        }
                    }
                })
        };
        UploadBucket bucket = new();

        await NewLocalFiles(bucket).RewriteSegmentsAsync(segments, TestContext.Current.CancellationToken);

        // 文本与摘要可能复读自群友输入，不能当作本地文件读取。
        Assert.Equal(file, segments[0]!["data"]!["text"]!.GetValue<string>());
        Assert.Equal(file, segments[1]!["data"]!["summary"]!.GetValue<string>());
        Assert.StartsWith(UploadBucket.URL_PREFIX, segments[1]!["data"]!["uri"]!.GetValue<string>());
        JsonNode video = segments[2]!["data"]!["messages"]![0]!["segments"]![0]!["data"]!;
        Assert.StartsWith(UploadBucket.URL_PREFIX, video["uri"]!.GetValue<string>());
        Assert.StartsWith(UploadBucket.URL_PREFIX, video["thumb_uri"]!.GetValue<string>());
        Assert.Equal(3, bucket.Keys.Count);
    }

    [Fact]
    public async Task RewriteAction_UploadsOnlyFileFieldsOfKnownActions()
    {
        string file = new Uri(CreateTempFile()).AbsoluteUri;
        JsonObject announcement = new() { ["content"] = file, ["image_uri"] = file };
        JsonObject other = new() { ["uri"] = file };
        UploadBucket bucket = new();
        LocalFiles files = NewLocalFiles(bucket);

        await files.RewriteActionAsync("send_group_announcement", announcement, TestContext.Current.CancellationToken);
        await files.RewriteActionAsync("get_group_info", other, TestContext.Current.CancellationToken);

        Assert.Equal(file, announcement["content"]!.GetValue<string>());
        Assert.StartsWith(UploadBucket.URL_PREFIX, announcement["image_uri"]!.GetValue<string>());
        Assert.Equal(file, other["uri"]!.GetValue<string>());
        Assert.Single(bucket.Keys);
    }

    private static LocalFiles NewLocalFiles(UploadBucket bucket) =>
        new(new BridgeContext(new TestAccessor(RecordingBotApi.Create().Api), bucket), TimeProvider.System, NullLogger<LocalFiles>.Instance);

    private static JsonObject Segment(string type, JsonObject data) =>
        new()
        {
            ["type"] = type,
            ["data"] = data
        };

    private static string CreateTempFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"shinano-nb2-{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }

    [Fact]
    public void Build_GroupMessageCarriesKeyAndMemberWithNeutralDefaults()
    {
        JsonObject payload = MilkyEvents.Build(GroupMessage(RecordingBotApi.Create().Api), "k1")!;

        Assert.Equal("message_receive", payload["event_type"]!.GetValue<string>());
        Assert.Equal("k1", payload[MilkyEvents.KEY_FIELD]!.GetValue<string>());
        JsonObject data = payload["data"]!.AsObject();
        Assert.Equal("group", data["message_scene"]!.GetValue<string>());
        Assert.Equal(GROUP_ID, data["peer_id"]!.GetValue<long>());
        Assert.Equal(SENDER, data["sender_id"]!.GetValue<long>());
        Assert.Null(data["friend"]);
        Assert.Equal(Nb2EventKind.GroupMessage, MilkyEvents.Classify(GroupMessage(RecordingBotApi.Create().Api)));
    }

    [Fact]
    public void Build_FriendMessageFillsFieldsSoraDoesNotKeep()
    {
        MessageReceivedEvent e = new()
        {
            Api     = RecordingBotApi.Create().Api,
            SelfId  = SELF_ID,
            Time    = DateTime.Now,
            Message = new MessageContext
            {
                MessageId  = 9,
                SourceType = MessageSourceType.Friend,
                SenderId   = SENDER,
                Time       = DateTime.Now,
                Body       = new MessageBody("在吗")
            }
        };

        JsonObject data = MilkyEvents.Build(e, "k2")!["data"]!.AsObject();

        Assert.Equal("friend", data["message_scene"]!.GetValue<string>());
        Assert.Equal(SENDER, data["peer_id"]!.GetValue<long>());
        // Sora 事件没有发送者资料时不附带 friend，适配器的 friend 为可选。
        Assert.Null(data["friend"]);
        Assert.Equal(Nb2EventKind.PrivateMessage, MilkyEvents.Classify(e));
    }

    [Fact]
    public void Classify_ExcludesTempSessions()
    {
        IBotApi api = RecordingBotApi.Create().Api;
        MessageReceivedEvent message = new()
        {
            Api     = api,
            SelfId  = SELF_ID,
            Message = new MessageContext
            {
                SourceType = MessageSourceType.Temp,
                SenderId   = SENDER,
                Body       = new MessageBody("在吗")
            }
        };

        Assert.Null(MilkyEvents.Classify(message));
        Assert.Null(MilkyEvents.Classify(new MessageDeletedEvent { Api = api, SourceType = MessageSourceType.Temp }));
        Assert.Equal(Nb2EventKind.Notice, MilkyEvents.Classify(new MessageDeletedEvent { Api = api, SourceType = MessageSourceType.Friend }));
    }

    [Fact]
    public void DotEnv_ReplacesInPlaceRemovesNullAndQuotesJson()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("shinano-nb2-env-").FullName, ".env");
        File.WriteAllLines(path, ["# 注释", "PARSER_DISABLED_PLATFORMS=[\"youtube\"]", "PARSER_PROXY=old", "OTHER=1"]);

        Nb2DotEnv.Save(
            path,
            new Dictionary<string, JsonNode?>
            {
                ["parser_disabled_platforms"] = new JsonArray("youtube", "tiktok"),
                ["parser_proxy"]              = null,
                ["parser_use_base64"]         = true,
                ["parser_custom_font"]        = "a\"b\\c"
            });

        Assert.Equal(
            ["# 注释", "PARSER_DISABLED_PLATFORMS=\"[\\\"youtube\\\",\\\"tiktok\\\"]\"", "OTHER=1", "PARSER_USE_BASE64=true", "PARSER_CUSTOM_FONT=\"a\\\"b\\\\c\""],
            File.ReadAllLines(path));
        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void LogSink_StripsColorsAndHidesToken()
    {
        using Nb2LogSink sink = new(Path.GetTempPath());

        sink.Write("\u001b[32m11-11\u001b[0m Error while setup websocket to ws://127.0.0.1:1/event?access_token=abc123");

        Assert.Equal("11-11 Error while setup websocket to ws://127.0.0.1:1/event?access_token=***", Assert.Single(sink.Recent(10)));
    }

    [Fact]
    public void Catalog_ReadsToolInitialPlugins()
    {
        string data = Directory.CreateTempSubdirectory("shinano-nb2-catalog-").FullName;
        try
        {
            Nb2Layout layout = new(data);
            Directory.CreateDirectory(layout.Root);
            File.WriteAllText(layout.PluginsFile, Nb2SetupCommand.InitialPlugins(DateTimeOffset.UnixEpoch));

            Nb2PluginEntry entry = Assert.Single(new Nb2Catalog(layout).Entries());
            Assert.Equal("nonebot_plugin_parser", entry.Module);
            Assert.False(entry.Enabled);
            Assert.Equal(DateTimeOffset.UnixEpoch, entry.InstalledAt);
            Assert.Equal(Nb2SetupCommand.DIRECTORY_NAME, Nb2Layout.DIRECTORY_NAME);
            Assert.Equal(Nb2SetupCommand.PLUGINS_FILE, Nb2Layout.PLUGINS_FILE);
            Assert.Equal(Nb2SetupCommand.LOCK_FILE, Nb2Layout.LOCK_FILE);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    [Fact]
    public async Task Server_RejectsWrongToken()
    {
        await using Harness harness = await Harness.StartAsync();
        using ClientWebSocket socket = new();
        await Assert.ThrowsAsync<WebSocketException>(
            () => socket.ConnectAsync(new Uri($"ws://127.0.0.1:{harness.Server.Port}/event?access_token=wrong"), TestContext.Current.CancellationToken));

        using HttpClient http = new();
        HttpResponseMessage response = await http.PostAsync(
            $"http://127.0.0.1:{harness.Server.Port}/api/get_login_info",
            new StringContent("{}", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Deliver_FollowsReportedResult(bool handled, bool continues)
    {
        await using Harness harness = await Harness.StartAsync();
        using ClientWebSocket socket = await harness.ConnectAsync();
        MessageReceivedEvent e = GroupMessage(harness.Api);

        Task delivery = harness.Relay.DeliverAsync(harness.Server, e, TimeSpan.FromSeconds(10));
        JsonObject received = await ReceiveAsync(socket);
        Assert.Equal("看看这个", received["data"]!["segments"]![0]!["data"]!["text"]!.GetValue<string>());
        await harness.CallAsync(
            MilkyActions.EVENT_DONE,
            new JsonObject
            {
                ["key"]     = received[MilkyEvents.KEY_FIELD]!.GetValue<string>(),
                ["handled"] = handled
            });

        await delivery;
        Assert.Equal(continues, e.IsContinueEventChain);
    }

    [Fact]
    public async Task Deliver_ContinuesAfterTimeout()
    {
        await using Harness harness = await Harness.StartAsync();
        using ClientWebSocket socket = await harness.ConnectAsync();
        MessageReceivedEvent e = GroupMessage(harness.Api);

        await harness.Relay.DeliverAsync(harness.Server, e, TimeSpan.FromMilliseconds(300));

        Assert.True(e.IsContinueEventChain);
        JsonObject received = await ReceiveAsync(socket);
        // 超时后的回报只被忽略。
        JsonObject reply = await harness.CallAsync(
            MilkyActions.EVENT_DONE,
            new JsonObject { ["key"] = received[MilkyEvents.KEY_FIELD]!.GetValue<string>(), ["handled"] = true });
        Assert.Equal("ok", reply["status"]!.GetValue<string>());
        Assert.True(e.IsContinueEventChain);
    }

    [Fact]
    public async Task SendGroupMessage_GoesThroughSora()
    {
        await using Harness harness = await Harness.StartAsync();

        JsonObject reply = await harness.CallAsync(
            "send_group_message",
            JsonNode.Parse("""{ "group_id": 123456, "message": [ { "type": "text", "data": { "text": "pong" } } ] }""")!.AsObject());

        Assert.Equal("ok", reply["status"]!.GetValue<string>());
        Assert.Equal(RecordingBotApi.MESSAGE_ID, reply["data"]!["message_seq"]!.GetValue<long>());
        (string method, object?[] arguments) = Assert.Single(harness.Recorder.Calls);
        Assert.Equal(nameof(IBotApi.SendGroupMessageAsync), method);
        Assert.Equal(GROUP_ID, (long)(GroupId)arguments[0]!);
        Assert.Equal("pong", Assert.IsType<TextSegment>(Assert.Single((MessageBody)arguments[1]!)).Text);
    }

    [Fact]
    public async Task OtherActions_ArePassedToLlBot()
    {
        await using Harness harness = await Harness.StartAsync();

        JsonObject reply = await harness.CallAsync("get_login_info", []);

        Assert.Equal(SELF_ID, reply["data"]!["uin"]!.GetValue<long>());
        Assert.Equal("get_login_info", harness.Accessor.LastAction);
    }

    // 进程每次都在就绪前退出：间隔 20 秒自动重启 2 次，第 2 次重启后再退出时停止拉起并只通知一次。
    // 原来按 1 分钟时间窗计数，而控制插件 60 秒才自行退出，窗口内永远只有 1 次，于是无限重启也不通知。
    [Fact]
    public async Task Runner_RestartsTwiceAtFixedInterval_ThenStopsAndNotifiesOnce()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "用系统自带的 where.exe 扮演启动后立即退出的 NB2 进程");
        string data = Directory.CreateTempSubdirectory("shinano-nb2-runner-").FullName;
        try
        {
            Nb2Layout layout = new(data);
            Directory.CreateDirectory(Path.GetDirectoryName(layout.Python) ?? data);
            File.Copy(Path.Combine(Environment.SystemDirectory, "where.exe"), layout.Python);
            RecordingNotices notices = new();
            BridgeContext context = new(new TestAccessor(RecordingBotApi.Create().Api), notices: notices);
            FakeTimeProvider time = new(DateTimeOffset.UtcNow);
            MilkyActions actions = new(context, new EventTracker(), new LocalFiles(context, time, NullLogger<LocalFiles>.Instance), time, NullLogger<MilkyActions>.Instance);
            using Nb2Runner runner = new(context, layout, new Nb2Catalog(layout), actions, time, NullLogger<Nb2Runner>.Instance);

            await runner.StartAsync(TestContext.Current.CancellationToken);
            // 每轮推进一个重启间隔；等待中的重启尚未登记计时器时推进不会生效，下一轮再推进。
            for (int round = 0; round < 200 && runner.Status().State != Nb2ProcessState.Failed; round++)
            {
                time.Advance(Nb2Runner.RestartDelay);
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }

            Nb2RunnerStatus status = runner.Status();
            Assert.Equal((Nb2ProcessState.Failed, Nb2Runner.MAX_AUTO_RESTARTS), (status.State, status.AutoRestarts));
            await Task.Delay(200, TestContext.Current.CancellationToken);
            Assert.Equal("NB2-STOPPED", Assert.Single(notices.Sent).Code);
        }
        finally
        {
            Directory.Delete(data, recursive: true);
        }
    }

    private static MessageReceivedEvent GroupMessage(IBotApi api) =>
        new()
        {
            Api     = api,
            SelfId  = SELF_ID,
            Time    = DateTime.Now,
            Message = new MessageContext
            {
                MessageId  = 77,
                SourceType = MessageSourceType.Group,
                GroupId    = GROUP_ID,
                SenderId   = SENDER,
                Time       = DateTime.Now,
                Body       = new MessageBody("看看这个")
            }
        };

    private static async Task<JsonObject> ReceiveAsync(ClientWebSocket socket)
    {
        byte[] buffer = new byte[64 * 1024];
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, TestContext.Current.CancellationToken);
        return JsonNode.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count))!.AsObject();
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(MilkyServer server, Nb2EventRelay relay, RecordingBotApi recorder, IBotApi api, TestAccessor accessor)
        {
            Server   = server;
            Relay    = relay;
            Recorder = recorder;
            Api      = api;
            Accessor = accessor;
        }

        public MilkyServer Server { get; }

        public Nb2EventRelay Relay { get; }

        public RecordingBotApi Recorder { get; }

        public IBotApi Api { get; }

        public TestAccessor Accessor { get; }

        public static async Task<Harness> StartAsync()
        {
            (IBotApi api, RecordingBotApi recorder) = RecordingBotApi.Create();
            TestAccessor accessor = new(api);
            BridgeContext context = new(accessor);
            EventTracker tracker = new();
            LocalFiles files = new(context, TimeProvider.System, NullLogger<LocalFiles>.Instance);
            MilkyActions actions = new(context, tracker, files, TimeProvider.System, NullLogger<MilkyActions>.Instance);
            MilkyServer server = await MilkyServer.StartAsync(actions, TOKEN, NullLogger.Instance, TestContext.Current.CancellationToken);
            // SAFE: 测试只调用 DeliverAsync，不经过用到进程守护的 HandleAsync。
            Nb2EventRelay relay = new(context, null!, tracker, NullLogger<Nb2EventRelay>.Instance);
            return new Harness(server, relay, recorder, api, accessor);
        }

        public async Task<ClientWebSocket> ConnectAsync()
        {
            ClientWebSocket socket = new();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Server.Port}/event?access_token={TOKEN}"), TestContext.Current.CancellationToken);
            while (!Server.IsConnected) await Task.Delay(10, TestContext.Current.CancellationToken);
            return socket;
        }

        public async Task<JsonObject> CallAsync(string action, JsonObject parameters)
        {
            using HttpClient http = new();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TOKEN);
            HttpResponseMessage response = await http.PostAsync(
                $"http://127.0.0.1:{Server.Port}/api/{action}",
                new StringContent(parameters.ToJsonString(), Encoding.UTF8, "application/json"),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))!.AsObject();
        }

        public ValueTask DisposeAsync() => Server.DisposeAsync();
    }

    /// <summary>记录调用的 IBotApi：发送消息成功返回固定消息 ID，其他调用不支持。</summary>
    public class RecordingBotApi : DispatchProxy
    {
        public const long MESSAGE_ID = 4242;

        public List<(string Method, object?[] Arguments)> Calls { get; } = [];

        public static (IBotApi Api, RecordingBotApi Recorder) Create()
        {
            IBotApi api = Create<IBotApi, RecordingBotApi>();
            return (api, (RecordingBotApi)(object)api);
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            // SAFE: DispatchProxy 调用接口方法时总会给出方法。
            MethodInfo method = targetMethod!;
            Calls.Add((method.Name, args ?? []));
            return method.ReturnType == typeof(ValueTask<SendMessageResult>)
                ? ValueTask.FromResult(SendMessageResult.Ok(MESSAGE_ID))
                : throw new NotSupportedException(method.Name);
        }
    }

    private sealed class TestAccessor : IBotAccessor
    {
        public TestAccessor(IBotApi api) => Api = api;

        public IBotApi? Api { get; }

        public long SelfId => SELF_ID;

        public IReadOnlyList<long> SuperUsers => [];

        public string? LastAction { get; private set; }

        public Task<MilkyCallResult> CallMilkyAsync(string action, JsonObject parameters, CancellationToken cancellationToken)
        {
            LastAction = action;
            return Task.FromResult(
                new MilkyCallResult
                {
                    StatusCode = 200,
                    Body       = $$$"""{"status":"ok","retcode":0,"data":{"uin":{{{SELF_ID}}},"nickname":"测试"}}"""
                });
        }
    }

    /// <summary>只记录上传的 bucket：预签名链接为固定前缀加对象 key。</summary>
    private sealed class UploadBucket : IPluginBuckets, IPluginBucket
    {
        public const string URL_PREFIX = "https://im.example.invalid/";

        public List<string> Keys { get; } = [];

        public string Name => "shinano-test-nb2";

        public IPluginBucket Get(string purpose) => this;

        public Task EnsureCreatedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<long> PutAsync(string key, Stream content, string mediaType, long? limitBytes, CancellationToken cancellationToken)
        {
            Keys.Add(key);
            await content.CopyToAsync(Stream.Null, cancellationToken);
            return content.Length;
        }

        public Task<string> CreateImUrlAsync(string key) => Task.FromResult(URL_PREFIX + key);

        public Task DeleteAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PluginBucketObject?> GetInfoAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();

        public IAsyncEnumerable<PluginBucketObject> ListAsync(string? prefix, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DeleteObjectAsync(string key, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>记录发给 Super User 的通知。</summary>
    private sealed class RecordingNotices : IPluginNotices
    {
        public List<SuperUserNotice> Sent { get; } = [];

        public void NotifySuperUsers(SuperUserNotice notice)
        {
            lock (Sent) Sent.Add(notice);
        }
    }

    /// <summary>素材一律为空文件，ffmpeg 一律不可用；进程守护测试只需要能写出脚本、启动进程。</summary>
    private sealed class EmptyAssets : IPluginAssets, IPluginTools
    {
        public Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken) => Task.FromResult(Array.Empty<byte>());

        public Task<string> FfmpegDirectoryAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("测试中不提供 ffmpeg");
    }

    private sealed class DefaultConfig<T> : IPluginConfig<T> where T : class, new()
    {
        public T Value { get; } = new();
    }

    private sealed class BridgeContext : IPluginInitializationContext
    {
        private readonly IPluginBuckets?  _buckets;
        private readonly IPluginNotices? _notices;
        private readonly EmptyAssets      _assets = new();

        public BridgeContext(IBotAccessor bot, IPluginBuckets? buckets = null, IPluginNotices? notices = null)
        {
            Bot      = bot;
            _buckets = buckets;
            _notices = notices;
        }

        public string PluginId => "nb2-bridge";

        public IPluginEvents Events => throw new NotSupportedException();

        public IPluginScheduler Scheduler => throw new NotSupportedException();

        public IPluginDatabase Database => throw new NotSupportedException();

        public IPluginMedia Media => throw new NotSupportedException();

        public IPluginBuckets Buckets => _buckets ?? throw new NotSupportedException();

        public IBotAccessor Bot { get; }

        public IPluginAssets Assets => _assets;

        public IPluginPanel Panel => throw new NotSupportedException();

        public IPluginFonts Fonts => throw new NotSupportedException();

        public IPluginImages Images => throw new NotSupportedException();

        public IPluginBrowser Browser => throw new NotSupportedException();

        public string DataDirectory => throw new NotSupportedException();

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public IPluginTools Tools => _assets;

        public void OnEnabledChanged(Action<bool> handler) => throw new NotSupportedException();

        public IPluginNotices Notices => _notices ?? throw new NotSupportedException();

        public IPluginConfig<T> Config<T>() where T : class, new() => new DefaultConfig<T>();

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }
}
