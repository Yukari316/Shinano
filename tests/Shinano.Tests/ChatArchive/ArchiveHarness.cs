using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Host.Archive;
using Shinano.Host.Bot;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.ChatArchive.Options;
using Shinano.Infrastructure.Configuration;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Scheduling;
using Shinano.Tests.Infrastructure;
using Shinano.Tests.Plugins;
using Sora;
using Sora.Adapter.Milky;
using Sora.Entities.Interfaces;
using Shinano.Tests.Rendering;

namespace Shinano.Tests.ChatArchive;

/// <summary>真实的归档服务与 Sora 服务，连接到 <see cref="FakeMilkyServer" />。</summary>
public sealed class ArchiveHarness : IAsyncDisposable
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);

    private ArchiveHarness(FakeMilkyServer server, SoraService service, ChatArchiveService archive, IBotApi api)
    {
        Server  = server;
        Service = service;
        Archive = archive;
        Api     = api;
    }

    /// <summary>协议端。</summary>
    public FakeMilkyServer Server { get; }

    /// <summary>Sora 服务。</summary>
    public SoraService Service { get; }

    /// <summary>归档服务。</summary>
    public ChatArchiveService Archive { get; }

    /// <summary>已连接的 API，用于让 Bot 发送消息。</summary>
    public IBotApi Api { get; }

    /// <summary>启动归档服务并连接。</summary>
    public static async Task<ArchiveHarness> StartAsync(HostInfrastructure infrastructure)
    {
        FakeMilkyServer server = await FakeMilkyServer.StartAsync(InfrastructureFixture.SELF_ID);
        BotAccessor bot = new();
        ChatArchiveService archive = new(
            infrastructure,
            new ChatArchiveOptions(),
            new HostScheduler(NullLogger<HostScheduler>.Instance, new FakeTimeProvider()),
            bot,
            new ArchiveGapTracker(),
            new SuperUserNotifier(bot, new DeploymentOptions(), TestRendering.Images, TimeProvider.System, NullLogger<SuperUserNotifier>.Instance),
            TimeProvider.System,
            NullLogger<ChatArchiveService>.Instance);
        await archive.StartAsync(CancellationToken.None);

        SoraService service = SoraServiceFactory.Instance.CreateMilkyService(
            new MilkyConfig
            {
                Host                = "127.0.0.1",
                Port                = server.Port,
                Prefix              = FakeMilkyServer.PREFIX,
                ReconnectInterval   = TimeSpan.Zero,
                AutoMarkMessageRead = false
            });
        service.UseMessageRecorder(archive);
        TaskCompletionSource<IBotApi> connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Events.OnConnected += e =>
        {
            bot.SetConnected(e.Api, e.SelfId);
            connected.TrySetResult(e.Api);
            return ValueTask.CompletedTask;
        };
        await service.StartAsync();
        IBotApi api = await connected.Task.WaitAsync(ConnectTimeout);
        return new ArchiveHarness(server, service, archive, api);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Service.StopAsync();
        await Service.DisposeAsync();
        await Archive.StopAsync(CancellationToken.None);
        Archive.Dispose();
        await Server.DisposeAsync();
    }
}
