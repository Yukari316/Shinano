using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Host.Bot;
using Shinano.Host.Configuration;
using Shinano.Host.Panel;
using Shinano.Host.Panel.Auth;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Staging;
using Shinano.Host.Restart;
using Shinano.Host.Web;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Storage;
using Shinano.Monitor.Web;
using Shinano.Tests.Host;
using Shinano.Tests.Host.Restart;
using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Deployment;
using Shinano.Tool.Plugins;
using Shinano.Tool.Run;
using Shinano.Tool.Setup;
using System.Text.Json.Nodes;

namespace Shinano.Tests.Tool;

/// <summary>工具不引用主工程，这里核对它维护的约定与宿主、监控一致。</summary>
public sealed class ToolContractTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("shinano-tool-contract-").FullName;

    [Fact]
    public void Constants_MatchHost()
    {
        Assert.Equal(PanelTokenStore.TOKEN_LENGTH, PanelToken.LENGTH);
        Assert.Equal(RestartOptions.SERVICE_RESTART_EXIT_CODE, HostContract.RESTART_EXIT_CODE);
        Assert.Equal(RestartCoordinator.SUPERVISED_VARIABLE, HostContract.SUPERVISED_VARIABLE);
        Assert.Equal(ApiRoutes.READINESS_PATH, HostContract.READINESS_PATH);
        Assert.Equal(DeploymentPaths.DATA_DIRECTORY_VARIABLE, DeploymentLayout.DATA_DIRECTORY_VARIABLE);
        Assert.Equal(MonitorSettings.DATA_DIRECTORY_VARIABLE, DeploymentLayout.DATA_DIRECTORY_VARIABLE);
        Assert.Equal(DeploymentPaths.CONFIG_FILE_NAME, DeploymentLayout.CONFIG_FILE_NAME);
        Assert.True(PanelToken.IsValid(PanelToken.Generate()));
    }

    [Fact]
    public void WrittenConfig_BindsToHostAndMonitorOptions()
    {
        string path = Path.Combine(_directory, DeploymentLayout.CONFIG_FILE_NAME);
        DeploymentConfig config = DeploymentConfig.Load(path);
        config.Set(ConfigKeys.TIME_ZONE, "Asia/Tokyo");
        config.Set(ConfigKeys.BUCKET_PREFIX, "shinano-a");
        config.Set(ConfigKeys.SUPER_USERS, new JsonArray(1001L, 1002L));
        config.Set(ConfigKeys.DATABASE_DSN, "Host=db;Database=d;Username=u");
        config.Set(ConfigKeys.RUSTFS_ENDPOINT, "http://s3:9000");
        config.Set(ConfigKeys.RUSTFS_IM_ENDPOINT, "http://s3-im:9000");
        config.Set(ConfigKeys.RUSTFS_ACCESS_KEY, "ak");
        config.Set(ConfigKeys.RUSTFS_SECRET_KEY, "sk");
        config.Set(ConfigKeys.LLBOT_HOST, "llbot");
        config.Set(ConfigKeys.LLBOT_PORT, 3015);
        config.Set(ConfigKeys.LLBOT_PREFIX, "milky");
        config.Set(ConfigKeys.LLBOT_ACCESS_TOKEN, "token");
        config.Set(ConfigKeys.In(ConfigKeys.WEB, ConfigKeys.LISTEN_ADDRESS), "10.0.0.5");
        config.Set(ConfigKeys.In(ConfigKeys.WEB, ConfigKeys.PORT), 19200);
        config.Set(ConfigKeys.In(ConfigKeys.WEB, ConfigKeys.CERTIFICATE_PATH), "panel.pfx");
        config.Set(ConfigKeys.In(ConfigKeys.WEB, ConfigKeys.CERTIFICATE_PASSWORD), "pfx");
        config.Set(ConfigKeys.PANEL_TOKEN, "t");
        config.Set(ConfigKeys.HOST_SERVICE_NAME, "ShinanoA");
        config.Set(ConfigKeys.In(ConfigKeys.MONITOR, ConfigKeys.LISTEN_ADDRESS), "10.0.0.6");
        config.Set(ConfigKeys.In(ConfigKeys.MONITOR, ConfigKeys.PORT), 19201);
        config.Set(ConfigKeys.MONITOR_SERVICE_NAME, "ShinanoMonitorA");
        config.Set(ConfigKeys.PLUGIN_DIRECTORY, "plugins-a");
        config.Save();

        IConfigurationRoot root = new ConfigurationBuilder().AddJsonFile(path).Build();
        DeploymentOptions deployment = Bind<DeploymentOptions>(root, DeploymentOptions.SECTION_NAME);
        Assert.Equal("Asia/Tokyo", deployment.TimeZone);
        Assert.Equal("shinano-a", deployment.BucketPrefix);
        Assert.Equal([1001L, 1002L], deployment.SuperUsers);
        Assert.Equal("Host=db;Database=d;Username=u", Bind<DatabaseOptions>(root, DatabaseOptions.SECTION_NAME).Dsn);

        RustFsOptions rustFs = Bind<RustFsOptions>(root, RustFsOptions.SECTION_NAME);
        Assert.Equal(("http://s3:9000", "http://s3-im:9000", "ak", "sk"), (rustFs.Endpoint, rustFs.ImEndpoint, rustFs.AccessKey, rustFs.SecretKey));
        LlBotOptions llBot = Bind<LlBotOptions>(root, LlBotOptions.SECTION_NAME);
        Assert.Equal(("llbot", 3015, "milky", "token"), (llBot.Host, llBot.Port, llBot.Prefix, llBot.AccessToken));
        WebOptions web = Bind<WebOptions>(root, WebOptions.SECTION_NAME);
        Assert.Equal(("10.0.0.5", 19200, "panel.pfx", "pfx"), (web.ListenAddress, web.Port, web.CertificatePath, web.CertificatePassword));
        Assert.Equal("t", root[ConfigKeys.PANEL_TOKEN]);
        Assert.Equal("ShinanoA", Bind<RestartOptions>(root, RestartOptions.SECTION_NAME).EffectiveServiceName);
        Assert.Equal(ConfigKeys.DEFAULT_PLUGIN_DIRECTORY, new PluginOptions().Directory);
        Assert.Equal("plugins-a", Bind<PluginOptions>(root, PluginOptions.SECTION_NAME).Directory);

        MonitorSettings monitor = MonitorSettings.Read(path, _directory);
        Assert.Equal(("10.0.0.6", 19201), (monitor.Listen.ListenAddress, monitor.Listen.Port));
        Assert.Equal("ShinanoMonitorA", monitor.ServiceName);
        Assert.Equal(ConfigKeys.DEFAULT_PANEL_PORT, WebOptions.DEFAULT_PORT);
        Assert.Equal(ConfigKeys.DEFAULT_STATUS_PORT, MonitorSettings.DEFAULT_PORT);
    }

    // 宿主配置页显示的监控默认值必须与监控实际使用的一致：两边互不引用，各自维护一份。
    [Fact]
    public void MonitorDefaults_MatchHostView()
    {
        MonitorSettings monitor = MonitorSettings.Read(null, _directory);
        MonitorOptions host = new();

        Assert.Equal((monitor.Listen.ListenAddress, monitor.Listen.Port), (host.ListenAddress, host.Port));
        Assert.Equal(MonitorSettings.DEFAULT_INTERVAL_SECONDS, host.IntervalSeconds);
        Assert.Equal(MonitorSettings.DEFAULT_MAX_LOG_MIB, host.MaxLogSizeMiB);
    }

    [Theory]
    [InlineData("postgres://role:p%40ss@db.local:5433/shinano")]
    [InlineData("Host=db.local;Port=5433;Database=shinano;Username=role;Password=p@ss")]
    public void DsnFormat_ParsesLikeHost(string dsn)
    {
        Npgsql.NpgsqlConnectionStringBuilder host = PostgresConnectionString.Parse(dsn);
        Npgsql.NpgsqlConnectionStringBuilder tool = DsnFormat.Parse(dsn);

        Assert.Equal((host.Host, host.Port, host.Database, host.Username, host.Password), (tool.Host, tool.Port, tool.Database, tool.Username, tool.Password));
    }

    // URI 写法的查询参数与 # 片段曾被静默丢弃（如 sslmode），宿主与工具都改为拒绝。
    [Theory]
    [InlineData("postgres://role:pass@db.local:5433/shinano?sslmode=verify-full")]
    [InlineData("postgres://role:pass@db.local:5433/shinano#ss")]
    public void DsnFormat_RejectsUriQueryLikeHost(string dsn)
    {
        Assert.Throws<NotSupportedException>(() => PostgresConnectionString.Parse(dsn));
        Assert.Throws<ToolException>(() => DsnFormat.Parse(dsn));
    }

    [Fact]
    public void StagedUpgrade_IsAppliedByHostRestartCoordinator_AndResultIsReadByTool()
    {
        using PackageFixture fixture = new();
        string installedDir = fixture.Install("CustomDir", "sample-a", "1.0.0");
        fixture.Install("sample-b", "sample-b", "1.0.0");
        fixture.CreateStaging().StageRemoval("sample-b", purgeData: true);

        string bundled = Path.Combine(_directory, "bundled");
        Directory.CreateDirectory(bundled);
        using (Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.1.0")))
        using (FileStream file = File.Create(Path.Combine(bundled, "sample-a.zip")))
            zip.CopyTo(file);

        DeploymentState state = new() { BundledPlugins = ["sample-a"] };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BundledUpgradeResult upgradeResult = BundledPluginSync.Upgrade(
            PluginPackages.ReadBundled(bundled),
            fixture.Settings.PluginRoot,
            fixture.DataDirectory,
            state,
            new ToolLog(Path.Combine(_directory, "tool.log"), null),
            now);

        Assert.Equal(now, upgradeResult.PlanCreatedAt);
        Assert.True(RestartPlanFile.Exists(fixture.DataDirectory));

        // 宿主首次启动直接消费工具写入的 restart/plan.json，只替换 sample-a，保留手动暂存的 sample-b。
        RestartCoordinator coordinator = CreateCoordinator(fixture);
        coordinator.PrepareStartup();
        Assert.Equal("1.1.0", Shinano.Host.Plugins.PluginManifest.Read(Path.Combine(installedDir, "plugin.json")).Version);
        Assert.True(coordinator.OnPluginsLoaded(new Dictionary<string, string?> { ["sample-a"] = null, ["sample-b"] = null }));

        PendingPluginChange remaining = Assert.Single(fixture.CreateStaging().Pending());
        Assert.Equal("sample-b", remaining.Change.PluginId);
        Assert.True(remaining.Change.PurgeData);

        // 工具可读回宿主写入的 restart/result.json。
        ToolRestartResult? toolResult = RestartPlanFile.ReadResult(fixture.DataDirectory);
        Assert.NotNull(toolResult);
        Assert.Equal(ToolRestartOutcome.Succeeded, toolResult.Outcome);
        Assert.Single(toolResult.Changes);
        Assert.Equal(("sample-a", "1.0.0", "1.1.0"), (toolResult.Changes[0].PluginId, toolResult.Changes[0].FromVersion, toolResult.Changes[0].ToVersion));
    }

    // 随包插件升级如果未通过宿主加载验证，下一次启动自动回滚，工具读回的结果为 RolledBack。
    [Fact]
    public void StagedUpgrade_LoadFailureRollsBack_AndToolDetectsRollback()
    {
        using PackageFixture fixture = new();
        string installedDir = fixture.Install("sample-a", "sample-a", "1.0.0");
        string bundled = Path.Combine(_directory, "bundled-rollback");
        Directory.CreateDirectory(bundled);
        using (Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.1.0")))
        using (FileStream file = File.Create(Path.Combine(bundled, "sample-a.zip")))
            zip.CopyTo(file);

        DeploymentState state = new() { BundledPlugins = ["sample-a"] };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BundledPluginSync.Upgrade(
            PluginPackages.ReadBundled(bundled),
            fixture.Settings.PluginRoot,
            fixture.DataDirectory,
            state,
            new ToolLog(Path.Combine(_directory, "tool.log"), null),
            now);

        try
        {
            RestartCoordinator first = CreateCoordinator(fixture);
            first.PrepareStartup();
            Assert.Equal("1.1.0", Shinano.Host.Plugins.PluginManifest.Read(Path.Combine(installedDir, "plugin.json")).Version);
            Assert.False(first.OnPluginsLoaded(new Dictionary<string, string?> { ["sample-a"] = "程序集加载失败" }));

            RestartCoordinator second = CreateCoordinator(fixture);
            second.PrepareStartup();
            Assert.Equal("1.0.0", Shinano.Host.Plugins.PluginManifest.Read(Path.Combine(installedDir, "plugin.json")).Version);

            ToolRestartResult? toolResult = RestartPlanFile.ReadResult(fixture.DataDirectory);
            Assert.NotNull(toolResult);
            Assert.Equal(ToolRestartOutcome.RolledBack, toolResult.Outcome);
            Assert.Contains("程序集加载失败", toolResult.Reason);
            Assert.False(UpgradeCommand.ReportRestartOutcome(fixture.DataDirectory, now, new ToolLog(Path.Combine(_directory, "tool.log"), null)));
        }
        finally
        {
            Environment.ExitCode = 0;
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static T Bind<T>(IConfiguration root, string section) where T : new() => root.GetSection(section).Get<T>() ?? new T();

    private static RestartCoordinator CreateCoordinator(PackageFixture fixture)
    {
        DeploymentPaths paths = new()
        {
            BaseDirectory = fixture.DataDirectory,
            DataDirectory = fixture.DataDirectory
        };
        RuntimeHealth health = new(TimeProvider.System);
        return new RestartCoordinator(
            paths,
            new RestartOptions { KeepBackup = true },
            fixture.Settings,
            fixture.CreateStaging(),
            fixture.CreatePurger(),
            new NoOpLifetime(),
            null,
            new SuperUserNotifier(
                new BotAccessor(new LlBotOptions(), new DeploymentOptions()),
                new DeploymentOptions(),
                TestRendering.Images,
                TimeProvider.System,
                NullLogger<SuperUserNotifier>.Instance),
            health,
            TimeProvider.System,
            NullLogger<RestartCoordinator>.Instance);
    }

    private sealed class NoOpLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
