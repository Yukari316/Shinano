using Microsoft.Extensions.Configuration;
using Shinano.Host.Bot;
using Shinano.Host.Configuration;
using Shinano.Host.Panel.Auth;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Staging;
using Shinano.Host.Restart;
using Shinano.Host.Web;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Storage;
using Shinano.Monitor.Web;
using Shinano.Tests.Host.Restart;
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
        Assert.Equal(DeploymentPaths.LOCAL_CONFIG_FILE_NAME, DeploymentLayout.LOCAL_CONFIG_FILE_NAME);
        Assert.True(PanelToken.IsValid(PanelToken.Generate()));
    }

    [Fact]
    public void WrittenConfig_BindsToHostAndMonitorOptions()
    {
        string path = Path.Combine(_directory, DeploymentLayout.LOCAL_CONFIG_FILE_NAME);
        LocalConfig config = LocalConfig.Load(path);
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

        MonitorSettings monitor = MonitorSettings.Load(path, _directory);
        Assert.Equal(("10.0.0.6", 19201), (monitor.Listen.ListenAddress, monitor.Listen.Port));
        Assert.Equal("ShinanoMonitorA", monitor.ServiceName);
        Assert.Equal(ConfigKeys.DEFAULT_PANEL_PORT, WebOptions.DEFAULT_PORT);
        Assert.Equal(ConfigKeys.DEFAULT_STATUS_PORT, MonitorSettings.DEFAULT_PORT);
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

    [Fact]
    public void StagedUpgrade_IsReadByHostStaging()
    {
        using PackageFixture fixture = new();
        fixture.Install("sample-a", "sample-a", "1.0.0");
        string bundled = Path.Combine(_directory, "bundled");
        Directory.CreateDirectory(bundled);
        using (Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.1.0")))
        using (FileStream file = File.Create(Path.Combine(bundled, "sample-a.zip")))
            zip.CopyTo(file);

        DeploymentState state = new() { BundledPlugins = ["sample-a"] };
        BundledPluginSync.Upgrade(
            PluginPackages.ReadBundled(bundled),
            fixture.Settings.PluginRoot,
            fixture.DataDirectory,
            state,
            new ToolLog(Path.Combine(_directory, "tool.log"), null),
            DateTimeOffset.UtcNow);

        PendingPluginChange change = Assert.Single(fixture.CreateStaging().Pending());
        Assert.Equal(PluginChangeKind.Upgrade, change.Change.Kind);
        Assert.Equal(("sample-a", "1.0.0", "1.1.0"), (change.Change.PluginId, change.Change.FromVersion, change.Change.ToVersion));
        Assert.True(File.Exists(Path.Combine(fixture.CreateStaging().PackageDirectory("sample-a"), "plugin.json")));
    }

    // 工具整体重写 pending.json，不能丢掉宿主写入的其他项的字段。
    [Fact]
    public void StagedUpgrade_KeepsOtherChangesPurgeFlag()
    {
        using PackageFixture fixture = new();
        fixture.Install("sample-a", "sample-a", "1.0.0");
        fixture.Install("sample-b", "sample-b", "1.0.0");
        fixture.CreateStaging().StageRemoval("sample-b", purgeData: true);
        string bundled = Path.Combine(_directory, "bundled");
        Directory.CreateDirectory(bundled);
        using (Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.1.0")))
        using (FileStream file = File.Create(Path.Combine(bundled, "sample-a.zip")))
            zip.CopyTo(file);

        BundledPluginSync.Upgrade(
            PluginPackages.ReadBundled(bundled),
            fixture.Settings.PluginRoot,
            fixture.DataDirectory,
            new DeploymentState { BundledPlugins = ["sample-a"] },
            new ToolLog(Path.Combine(_directory, "tool.log"), null),
            DateTimeOffset.UtcNow);

        PendingPluginChange removal = Assert.Single(fixture.CreateStaging().Pending(), item => item.Change.PluginId == "sample-b");
        Assert.True(removal.Change.PurgeData);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static T Bind<T>(IConfiguration root, string section) where T : new() => root.GetSection(section).Get<T>() ?? new T();
}
