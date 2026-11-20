using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Monitor.Probing;
using Shinano.Monitor.Status;
using Shinano.Monitor.Web;
using System.Net;
using System.Net.Sockets;

namespace Shinano.Tests.Monitor;

public sealed class MonitorTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 10, 0, TimeSpan.Zero);

    private readonly string _directory = Directory.CreateTempSubdirectory("shinano-monitor-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Report_AlignsBarsToHalfHours_TakesWorstLevel_AndCountsDegradedAsAvailable()
    {
        ProbeRecord[] records =
        [
            Record(MonitoredService.Database, Now.AddMinutes(-5), HealthLevel.Normal),
            Record(MonitoredService.Database, Now.AddMinutes(-4), HealthLevel.Stopped),
            Record(MonitoredService.Database, Now.AddMinutes(-40), HealthLevel.Degraded),
            Record(MonitoredService.Database, Now.AddDays(-3), HealthLevel.Stopped)
        ];

        StatusReport report = StatusReport.Create(records, Now, Settings());

        ServiceStatus database = report.Services.Single(status => status.Service == MonitoredService.Database);
        Assert.Equal(StatusReport.BAR_COUNT, database.Bars.Count);
        StatusBar current = database.Bars[^1];
        Assert.Equal(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero), current.Start);
        Assert.Equal(HealthLevel.Stopped, current.Level);
        Assert.Equal(2, current.Total);
        Assert.Equal(1, current.Stopped);
        Assert.Equal(HealthLevel.Degraded, database.Bars[^2].Level);
        Assert.Null(database.Bars[0].Level);
        Assert.Equal(2.0 / 3, database.Availability24h);
        Assert.Equal(2.0 / 4, database.Availability7d);
        Assert.Equal(HealthLevel.Stopped, database.Level);
    }

    [Fact]
    public void Overall_TreatsObjectStorageOutageAsDegraded()
    {
        ProbeRecord[] storageDown =
        [
            Record(MonitoredService.Host, Now, HealthLevel.Normal),
            Record(MonitoredService.ObjectStorage, Now, HealthLevel.Stopped)
        ];
        Assert.Equal(HealthLevel.Degraded, StatusReport.Create(storageDown, Now, Settings()).Overall);

        ProbeRecord[] botDown = [.. storageDown, Record(MonitoredService.Bot, Now, HealthLevel.Stopped)];
        Assert.Equal(HealthLevel.Stopped, StatusReport.Create(botDown, Now, Settings()).Overall);
        Assert.Null(StatusReport.Create([], Now, Settings()).Overall);
    }

    [Fact]
    public void Store_PersistsByLocalDate_ReloadsAndPrunesExpiredFiles()
    {
        TimeZoneInfo shanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        ProbeStore store = new(_directory, shanghai, NullLogger<ProbeStore>.Instance);
        // 16:30 UTC 已是上海的次日。
        DateTimeOffset late = new(2026, 10, 22, 16, 30, 0, TimeSpan.Zero);
        store.Append([Record(MonitoredService.Bot, late, HealthLevel.Normal)], late);
        File.WriteAllText(Path.Combine(_directory, "probes-2026-10-01.jsonl"), "{}\n");

        ProbeStore reloaded = new(_directory, shanghai, NullLogger<ProbeStore>.Instance);
        reloaded.Load(late.AddMinutes(1));

        Assert.True(File.Exists(Path.Combine(_directory, "probes-2026-10-23.jsonl")));
        Assert.False(File.Exists(Path.Combine(_directory, "probes-2026-10-01.jsonl")));
        ProbeRecord record = Assert.Single(reloaded.Snapshot());
        Assert.Equal(late, record.Time);
        Assert.Equal(HealthLevel.Normal, record.Level);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("not-an-address")]
    public void Listen_RejectsWildcardOrInvalidAddress(string address)
    {
        ListenEndpoint endpoint = ListenEndpoint.Resolve(new ListenOptions { ListenAddress = address }, MonitorSettings.DEFAULT_PORT, _directory);

        Assert.Equal(IPAddress.Loopback, endpoint.Address);
        Assert.Equal(MonitorSettings.DEFAULT_PORT, endpoint.Port);
        Assert.NotEmpty(endpoint.Problems);
    }

    [Fact]
    public void Listen_UsesHttpOnNonLoopback_ButFallsBackWhenCertificateCannotLoad()
    {
        ListenEndpoint plain = ListenEndpoint.Resolve(new ListenOptions { ListenAddress = "192.0.2.5", Port = 19101 }, MonitorSettings.DEFAULT_PORT, _directory);
        Assert.Equal("http://192.0.2.5:19101", plain.BaseUrl);

        ListenEndpoint broken = ListenEndpoint.Resolve(
            new ListenOptions
            {
                ListenAddress   = "192.0.2.5",
                Port            = 19101,
                CertificatePath = "missing.pfx"
            },
            MonitorSettings.DEFAULT_PORT,
            _directory);
        Assert.Equal("http://127.0.0.1:19101", broken.BaseUrl);
        Assert.NotEmpty(broken.Problems);
    }

    [Fact]
    public async Task Prober_ClassifiesHostPhases_HttpErrors_AndUnreachableServices()
    {
        await using WebApplication fake = await StartFakeServicesAsync();
        // SAFE: Kestrel 启动后总会提供监听地址特性。
        int port = new Uri(fake.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
        using ServiceProber prober = new(
            Settings(hostPort: port, objectStorage: $"http://127.0.0.1:{port}/", llBotPort: port),
            new FakeTimeProvider(Now),
            NullLogger.Instance);
        CancellationToken token = TestContext.Current.CancellationToken;

        ProbeRecord host = await prober.ProbeAsync(MonitoredService.Host, token);
        Assert.Equal(HealthLevel.Stopped, host.Level);
        Assert.Equal(ProbeFailure.FaultMode, host.Failure);

        ProbeRecord storage = await prober.ProbeAsync(MonitoredService.ObjectStorage, token);
        Assert.Equal(ProbeFailure.HttpStatus, storage.Failure);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, storage.StatusCode);

        ProbeRecord bot = await prober.ProbeAsync(MonitoredService.Bot, token);
        Assert.Equal(ProbeFailure.AuthenticationFailed, bot.Failure);

        ProbeRecord database = await prober.ProbeAsync(MonitoredService.Database, token);
        Assert.Equal(ProbeFailure.NotConfigured, database.Failure);
    }

    // LLBot 主机填 IPv6 地址时拼出的 URI 无效，抛出的 UriFormatException 曾让探测循环与监控进程退出。
    [Fact]
    public async Task Prober_ClassifiesUnexpectedExceptionAsError()
    {
        using ServiceProber prober = new(Settings(llBotHost: "::1"), new FakeTimeProvider(Now), NullLogger.Instance);

        ProbeRecord bot = await prober.ProbeAsync(MonitoredService.Bot, TestContext.Current.CancellationToken);

        Assert.Equal((HealthLevel.Stopped, ProbeFailure.Error), (bot.Level, bot.Failure));
    }

    [Fact]
    public async Task ProbeService_ProbesNewHostAddress_AfterDeploymentConfigChanges()
    {
        await using WebApplication fake = await StartFakeServicesAsync();
        // SAFE: Kestrel 启动后总会提供监听地址特性。
        int hostPort = new Uri(fake.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
        using TcpListener closed = new(IPAddress.Loopback, 0);
        closed.Start();
        int closedPort = ((IPEndPoint)closed.LocalEndpoint).Port;
        closed.Stop();
        string configPath = Path.Combine(_directory, MonitorSettings.CONFIG_FILE_NAME);
        await File.WriteAllTextAsync(configPath, $$"""{ "Shinano": { "Web": { "Port": {{closedPort}} } } }""", TestContext.Current.CancellationToken);
        MonitorSettings settings = MonitorSettings.Load(configPath, _directory);
        FakeTimeProvider time = new(Now);
        ProbeStore store = new(Path.Combine(_directory, "probes"), TimeZoneInfo.Utc, NullLogger<ProbeStore>.Instance);
        using ProbeService service = new(
            store,
            settings,
            new LogRetention(Path.Combine(_directory, "logs"), "run", 1, NullLogger<LogRetention>.Instance),
            time,
            NullLogger<ProbeService>.Instance);

        await service.StartAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(await WaitForHostAsync(store, record => record.Failure != ProbeFailure.FaultMode, time, advance: false));
            // 面板改了监听端口：宿主以临时文件替换写入部署配置。
            string temporary = configPath + ".tmp";
            await File.WriteAllTextAsync(temporary, $$"""{ "Shinano": { "Web": { "Port": {{hostPort}} } } }""", TestContext.Current.CancellationToken);
            File.Move(temporary, configPath, overwrite: true);

            Assert.True(await WaitForHostAsync(store, record => record.Failure == ProbeFailure.FaultMode, time, advance: true));
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    // 等待出现满足条件的主进程探测记录；advance 为 true 时每次推进一个探测间隔以触发下一轮。
    private static async Task<bool> WaitForHostAsync(ProbeStore store, Func<ProbeRecord, bool> match, FakeTimeProvider time, bool advance)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (store.Snapshot().Any(record => record.Service == MonitoredService.Host && match(record))) return true;
            if (advance) time.Advance(TimeSpan.FromSeconds(30));
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static async Task<WebApplication> StartFakeServicesAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        app.MapGet("/healthz", () => Results.Ok(new { phase = "FaultMode", overall = "Stopped" }));
        app.MapGet("/health", () => Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
        app.MapPost("/milky/api/get_login_info", (HttpContext context) =>
            context.Request.Headers.Authorization == "Bearer right" ? Results.Ok() : Results.Unauthorized());
        await app.StartAsync();
        return app;
    }

    private static ProbeRecord Record(MonitoredService service, DateTimeOffset time, HealthLevel level) =>
        new()
        {
            Time      = time,
            Service   = service,
            Level     = level,
            LatencyMs = 1
        };

    private MonitorSettings Settings(
        int     hostPort      = MonitorSettings.DEFAULT_HOST_PORT,
        string? objectStorage = null,
        int     llBotPort     = 3000,
        string  llBotHost     = "127.0.0.1") =>
        new()
        {
            DataDirectory         = _directory,
            Listen                = new ListenOptions { Port = MonitorSettings.DEFAULT_PORT },
            Interval              = TimeSpan.FromSeconds(30),
            MaxLogSizeMiB         = 256,
            ServiceName           = "ShinanoMonitorTest",
            TimeZone              = TimeZoneInfo.Utc,
            HostWeb               = new ListenOptions { Port = hostPort },
            ObjectStorageEndpoint = objectStorage,
            LlBotHost             = llBotHost,
            LlBotPort             = llBotPort,
            LlBotPrefix           = "milky",
            LlBotAccessToken      = "wrong"
        };
}
