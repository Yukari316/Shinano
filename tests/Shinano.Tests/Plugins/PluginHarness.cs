using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Host.Bot;
using Shinano.Host.Plugins;
using Shinano.Infrastructure;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Scheduling;
using Shinano.Tests.Infrastructure;
using Sora;
using Sora.Adapter.Milky;
using Sora.Core.Types;

namespace Shinano.Tests.Plugins;

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

    private readonly string _dataDirectory;

    private PluginHarness(FakeMilkyServer server, SoraService service, PluginRuntime runtime, string dataDirectory)
    {
        Server         = server;
        Service        = service;
        Runtime        = runtime;
        _dataDirectory = dataDirectory;
    }

    /// <summary>协议端。</summary>
    public FakeMilkyServer Server { get; }

    /// <summary>Sora 服务。</summary>
    public SoraService Service { get; }

    /// <summary>插件运行时。</summary>
    public PluginRuntime Runtime { get; }

    /// <summary>
    /// 创建并连接。<paramref name="pluginDirectory" /> 为相对仓库根目录的插件目录；本次涉及插件的持久化状态先被清除。
    /// </summary>
    public static async Task<PluginHarness> StartAsync(
        HostInfrastructure infrastructure,
        string             bucketPrefix,
        string             pluginDirectory,
        params string[]    pluginIds)
    {
        infrastructure.Database.Global.CodeFirst.InitTables<PluginStateRecord, PluginConfigRecord>();
        await infrastructure.Database.Global.Deleteable<PluginStateRecord>()
                            .Where(state => pluginIds.Contains(state.PluginId))
                            .ExecuteCommandAsync();

        FakeMilkyServer server = await FakeMilkyServer.StartAsync(InfrastructureFixture.SELF_ID);
        BotAccessor bot = new();
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
        return new PluginHarness(server, service, runtime, dataDirectory);
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
        Directory.Delete(_dataDirectory, recursive: true);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Shinano.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("找不到仓库根目录");
    }
}
