using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Host.Bot;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Purge;
using Shinano.Host.Plugins.Staging;
using Shinano.Host.Tools;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Storage;
using System.IO.Compression;
using System.Text.Json;

namespace Shinano.Tests.Host.Restart;

/// <summary>临时部署数据目录、插件目录与插件包的构造。</summary>
internal sealed class PackageFixture : IDisposable
{
    public PackageFixture(string bucketPrefix = "shinano-test")
    {
        DataDirectory = Directory.CreateTempSubdirectory("shinano-restart-").FullName;
        Settings = new PluginRuntimeSettings
        {
            DataDirectory   = DataDirectory,
            PluginDirectory = "plugins",
            BucketPrefix    = bucketPrefix
        };
        Directory.CreateDirectory(Settings.PluginRoot);
    }

    public string DataDirectory { get; }

    public PluginRuntimeSettings Settings { get; }

    public PluginPackageStaging CreateStaging() =>
        new(Settings, TimeProvider.System, NullLogger<PluginPackageStaging>.Instance);

    /// <summary>
    /// 插件数据清除服务。<paramref name="infrastructure" /> 为 null 时使用未初始化的基础设施：只建立任务，不会执行。
    /// </summary>
    public PluginDataPurger CreatePurger(HostInfrastructure? infrastructure = null, RuntimeHealth? health = null)
    {
        RuntimeHealth runtimeHealth = health ?? new RuntimeHealth(TimeProvider.System);
        return new PluginDataPurger(
            infrastructure ?? Uninitialized(runtimeHealth),
            Settings,
            new BrowserService(new BrowserOptions(), DataDirectory, TimeProvider.System, NullLogger<BrowserService>.Instance),
            new BotAccessor(new LlBotOptions(), new DeploymentOptions()),
            new SuperUserNotifier(new BotAccessor(new LlBotOptions(), new DeploymentOptions()), new DeploymentOptions(), TestRendering.Images, TimeProvider.System, NullLogger<SuperUserNotifier>.Instance),
            runtimeHealth,
            TimeProvider.System,
            NullLogger<PluginDataPurger>.Instance);
    }

    /// <summary>改写已安装插件包的清单，声明数据表。</summary>
    public void DeclareTables(string directory, string id, string version, string[] global, string[] account, params string[] bucketPurposes) =>
        File.WriteAllText(
            Path.Combine(directory, PluginManifest.FILE_NAME),
            JsonSerializer.Serialize(
                new
                {
                    id,
                    name          = id,
                    version,
                    entryAssembly = "Sample.dll",
                    bucketPurposes,
                    tables = new
                    {
                        global,
                        account
                    }
                }));

    public static Dictionary<string, string> Package(string id, string version, params string[] bucketPurposes) =>
        new()
        {
            ["plugin.json"] = Manifest(id, version, bucketPurposes),
            ["Sample.dll"]  = version,
            ["assets/a.txt"] = "asset"
        };

    public static Stream Zip(IEnumerable<KeyValuePair<string, string>> entries)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(stream, ZipArchiveMode.Create, leaveOpen: true))
            foreach ((string name, string content) in entries)
            {
                using StreamWriter writer = new(archive.CreateEntry(name).Open());
                writer.Write(content);
            }

        stream.Position = 0;
        return stream;
    }

    /// <summary>直接写入一个已安装的插件包目录。</summary>
    public string Install(string directoryName, string id, string version, params string[] bucketPurposes)
    {
        string directory = Path.Combine(Settings.PluginRoot, directoryName);
        Directory.CreateDirectory(directory);
        foreach ((string name, string content) in Package(id, version, bucketPurposes))
        {
            string path = Path.Combine(directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? directory);
            File.WriteAllText(path, content);
        }

        return directory;
    }

    public void Dispose() => Directory.Delete(DataDirectory, recursive: true);

    private static HostInfrastructure Uninitialized(RuntimeHealth health) =>
        new(
            new HostInfrastructureSettings
            {
                Deployment  = new DeploymentOptions(),
                Database    = new DatabaseOptions(),
                RustFs      = new RustFsOptions(),
                Media       = new MediaOptions(),
                ChatArchive = new ChatArchiveOptions()
            },
            health,
            new HostScheduler(NullLogger<HostScheduler>.Instance, new FakeTimeProvider()),
            TimeProvider.System,
            NullLoggerFactory.Instance);

    private static string Manifest(string id, string version, string[] bucketPurposes) =>
        JsonSerializer.Serialize(
            new
            {
                id,
                name           = id,
                version,
                entryAssembly  = "Sample.dll",
                bucketPurposes
            });
}
