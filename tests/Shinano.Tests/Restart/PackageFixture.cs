using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;
using System.IO.Compression;
using System.Text.Json;

namespace Shinano.Tests.Restart;

/// <summary>临时部署数据目录、插件目录与插件包的构造。</summary>
internal sealed class PackageFixture : IDisposable
{
    public PackageFixture()
    {
        DataDirectory = Directory.CreateTempSubdirectory("shinano-restart-").FullName;
        Settings = new PluginRuntimeSettings
        {
            DataDirectory   = DataDirectory,
            PluginDirectory = "plugins",
            BucketPrefix    = "shinano-test"
        };
        Directory.CreateDirectory(Settings.PluginRoot);
    }

    public string DataDirectory { get; }

    public PluginRuntimeSettings Settings { get; }

    public PluginPackageStaging CreateStaging() =>
        new(Settings, TimeProvider.System, NullLogger<PluginPackageStaging>.Instance);

    public static Dictionary<string, string> Package(string id, string version, params string[] bucketPurposes) =>
        new()
        {
            ["plugin.json"] = Manifest(id, version, bucketPurposes),
            ["Sample.dll"]  = version,
            ["assets/a.txt"] = "asset"
        };

    public static Stream Zip(IReadOnlyDictionary<string, string> entries)
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
