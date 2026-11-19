using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Host.Bot;
using Shinano.Host.Panel;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Config;
using Shinano.Host.Tools;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Shinano.Tests.Support;
using Sora;
using Sora.Adapter.Milky;
using Sora.Core.Types;

namespace Shinano.Tests.Host.Plugins;

/// <summary>
/// 真实的插件运行时与 Sora 服务，连接到 <see cref="FakeMilkyServer" />。插件按插件包从构建产物目录加载。
/// </summary>
public sealed class PluginHarness : IAsyncDisposable
{
    /// <summary>测试群。</summary>
    public const long GROUP_ID = 111;

    /// <summary>另一个测试群。</summary>
    public const long OTHER_GROUP_ID = 112;

    /// <summary>普通成员。</summary>
    public const long MEMBER_ID = 222;

    /// <summary>群管理员。</summary>
    public const long ADMIN_ID = 333;

    /// <summary>Super User（在群中是普通成员）。</summary>
    public const long SUPER_USER_ID = 444;

    private static readonly TimeSpan ReplyTimeout   = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SilenceTimeout = TimeSpan.FromMilliseconds(600);

    private PluginHarness(FakeMilkyServer server, SoraService service, PluginRuntime runtime, BotAccessor bot, string dataDirectory)
    {
        Server        = server;
        Service       = service;
        Runtime       = runtime;
        Bot           = bot;
        DataDirectory = dataDirectory;
    }

    /// <summary>协议端。</summary>
    public FakeMilkyServer Server { get; }

    /// <summary>Sora 服务。</summary>
    public SoraService Service { get; }

    /// <summary>插件运行时。</summary>
    public PluginRuntime Runtime { get; }

    /// <summary>当前连接。</summary>
    public BotAccessor Bot { get; }

    /// <summary>插件运行时使用的部署数据目录（临时目录，结束时删除）。</summary>
    public string DataDirectory { get; }

    /// <summary>
    /// 创建并连接。<paramref name="pluginDirectory" /> 为相对仓库根目录的插件目录；本次涉及插件的持久化状态先被清除。
    /// </summary>
    public static async Task<PluginHarness> StartAsync(
        HostInfrastructure infrastructure,
        string             bucketPrefix,
        string             pluginDirectory,
        params string[]    pluginIds) =>
        await StartAsync(infrastructure, bucketPrefix, pluginDirectory, null, pluginIds);

    /// <summary>
    /// 创建并连接，插件管理 API 登记到 <paramref name="endpoints" />；为 null 时使用不对外提供的独立端点来源。
    /// 与宿主一样，连接后视为机器人账号已就绪（测试夹具已初始化账号数据）。
    /// </summary>
    public static Task<PluginHarness> StartAsync(
        HostInfrastructure        infrastructure,
        string                    bucketPrefix,
        string                    pluginDirectory,
        PluginEndpointDataSource? endpoints,
        params string[]           pluginIds) =>
        CreateAsync(infrastructure, bucketPrefix, pluginDirectory, endpoints, accountReady: true, pluginIds);

    /// <summary>创建并连接，但不通知运行时机器人账号已就绪；由测试自行调用 <see cref="PluginRuntime.EnablePersistedAsync" />。</summary>
    public static Task<PluginHarness> StartBeforeAccountReadyAsync(
        HostInfrastructure        infrastructure,
        string                    bucketPrefix,
        string                    pluginDirectory,
        PluginEndpointDataSource? endpoints,
        params string[]           pluginIds) =>
        CreateAsync(infrastructure, bucketPrefix, pluginDirectory, endpoints, accountReady: false, pluginIds);

    private static async Task<PluginHarness> CreateAsync(
        HostInfrastructure        infrastructure,
        string                    bucketPrefix,
        string                    pluginDirectory,
        PluginEndpointDataSource? endpoints,
        bool                      accountReady,
        string[]                  pluginIds)
    {
        infrastructure.Database.Global.CodeFirst.InitTables<PluginStateRecord, PluginConfigRecord>();
        await infrastructure.Database.Global.Deleteable<PluginStateRecord>()
                            .Where(state => pluginIds.Contains(state.PluginId))
                            .ExecuteCommandAsync();

        FakeMilkyServer server = await FakeMilkyServer.StartAsync(InfrastructureFixture.SELF_ID);
        BotAccessor bot = new(new LlBotOptions(), new DeploymentOptions());
        string dataDirectory = Directory.CreateTempSubdirectory("shinano-plugins-").FullName;
        PluginRuntime runtime = new(
            infrastructure,
            new HostScheduler(NullLogger<HostScheduler>.Instance, new FakeTimeProvider()),
            bot,
            new RuntimeHealth(TimeProvider.System),
            new PluginRuntimeSettings
            {
                DataDirectory   = dataDirectory,
                PluginDirectory = Path.Combine(RepositoryRoot(), pluginDirectory),
                BucketPrefix    = bucketPrefix
            },
            endpoints ?? new PluginEndpointDataSource(new ServiceCollection().BuildServiceProvider()),
            TestRendering.Fonts,
            TestRendering.Images,
            new BrowserService(new BrowserOptions(), dataDirectory, TimeProvider.System, NullLogger<BrowserService>.Instance),
            new FfmpegService(new FfmpegOptions(), dataDirectory, NullLogger<FfmpegService>.Instance),
            new SuperUserNotifier(bot, new DeploymentOptions(), TestRendering.Images, TimeProvider.System, NullLogger<SuperUserNotifier>.Instance),
            NullLoggerFactory.Instance);
        await runtime.LoadAsync(CancellationToken.None);

        SoraService service = SoraServiceFactory.Instance.CreateMilkyService(
            new MilkyConfig
            {
                Host                = "127.0.0.1",
                Port                = server.Port,
                Prefix              = FakeMilkyServer.PREFIX,
                ReconnectInterval   = TimeSpan.Zero,
                AutoMarkMessageRead = false,
                SuperUsers          = [(UserId)SUPER_USER_ID]
            });
        runtime.Attach(service);
        TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Events.OnConnected += e =>
        {
            bot.SetConnected(e.Api, e.SelfId);
            connected.TrySetResult();
            return ValueTask.CompletedTask;
        };
        await service.StartAsync();
        await connected.Task.WaitAsync(ReplyTimeout);
        if (accountReady) await runtime.EnablePersistedAsync(CancellationToken.None);
        return new PluginHarness(server, service, runtime, bot, dataDirectory);
    }

    /// <summary>断言下一条回复的文本。</summary>
    public async Task<SentMessage> ExpectReplyAsync(string text)
    {
        SentMessage? sent = await Server.WaitForSentAsync(ReplyTimeout, TestContext.Current.CancellationToken);
        Assert.NotNull(sent);
        Assert.Equal(text, sent.Text);
        return sent;
    }

    /// <summary>断言一段时间内没有回复。</summary>
    public async Task ExpectSilenceAsync()
    {
        SentMessage? sent = await Server.WaitForSentAsync(SilenceTimeout, TestContext.Current.CancellationToken);
        Assert.Null(sent);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Service.StopAsync();
        await Service.DisposeAsync();
        await Server.DisposeAsync();
        Runtime.Dispose();
        Directory.Delete(DataDirectory, recursive: true);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shinano.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
