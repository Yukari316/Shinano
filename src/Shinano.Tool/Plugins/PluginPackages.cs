using System.IO.Compression;
using System.Text.Json;
using Shinano.Tool.Cli;
using Shinano.Tool.Plugins.Models;

namespace Shinano.Tool.Plugins;

/// <summary>插件包 zip 与插件目录的读取和解压。</summary>
public static class PluginPackages
{
    private const string ARCHIVE_PATTERN = "*.zip";
    private const char   HIDDEN_PREFIX   = '.';

    /// <summary>读取目录中的全部插件包；目录不存在时为空。</summary>
    /// <exception cref="ToolException">某个插件包缺少或含有无效的清单。</exception>
    public static IReadOnlyList<BundledPlugin> ReadBundled(string directory)
    {
        if (!Directory.Exists(directory)) return [];
        return
        [
            .. Directory.EnumerateFiles(directory, ARCHIVE_PATTERN)
                        .Order(StringComparer.Ordinal)
                        .Select(path => new BundledPlugin
                        {
                            ArchivePath = path,
                            Manifest    = ReadArchiveManifest(path)
                        })
        ];
    }

    /// <summary>
    /// 插件目录中已安装的插件，按 ID 索引。以 <c>.</c> 开头的目录（如插件包备份）与没有有效清单的目录不计入。
    /// </summary>
    public static IReadOnlyDictionary<string, InstalledPlugin> ScanInstalled(string pluginRoot)
    {
        Dictionary<string, InstalledPlugin> installed = new(StringComparer.Ordinal);
        if (!Directory.Exists(pluginRoot)) return installed;

        foreach (string directory in Directory.EnumerateDirectories(pluginRoot).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(directory).StartsWith(HIDDEN_PREFIX)) continue;
            string manifestPath = Path.Combine(directory, PluginManifest.FILE_NAME);
            if (!File.Exists(manifestPath)) continue;
            try
            {
                if (JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(manifestPath)) is { Id.Length: > 0 } manifest)
                    installed.TryAdd(
                        manifest.Id,
                        new InstalledPlugin
                        {
                            Directory = directory,
                            Manifest  = manifest
                        });
            }
            catch (JsonException)
            {
                // 清单无效的目录宿主同样不会加载，这里不计入已安装。
            }
        }

        return installed;
    }

    /// <summary>解压插件包到 <paramref name="destination" />，拒绝越出目标目录的条目。</summary>
    /// <exception cref="ToolException">插件包含有越出目标目录的条目。</exception>
    public static void Extract(string archivePath, string destination)
    {
        string root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(root);
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string path = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!path.StartsWith(root, StringComparison.Ordinal))
                throw new ToolException($"插件包 {archivePath} 的条目 {entry.FullName} 越出插件目录");
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(path);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
            entry.ExtractToFile(path, overwrite: true);
        }
    }

    /// <summary>
    /// <paramref name="candidate" /> 是否比 <paramref name="installed" /> 新；任一版本不是 <c>主.次[.修订]</c> 形式时为 null。
    /// </summary>
    public static bool? IsNewer(string candidate, string installed) =>
        Version.TryParse(candidate, out Version? next) && Version.TryParse(installed, out Version? current) ? next > current : null;

    private static PluginManifest ReadArchiveManifest(string archivePath)
    {
        try
        {
            using ZipArchive archive = ZipFile.OpenRead(archivePath);
            ZipArchiveEntry entry = archive.GetEntry(PluginManifest.FILE_NAME)
                                    ?? throw new ToolException($"插件包 {archivePath} 根目录缺少 {PluginManifest.FILE_NAME}");
            using Stream stream = entry.Open();
            return JsonSerializer.Deserialize<PluginManifest>(stream) is { Id.Length: > 0 } manifest
                ? manifest
                : throw new ToolException($"插件包 {archivePath} 的清单缺少插件 ID");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException)
        {
            throw new ToolException($"插件包 {archivePath} 无效：{exception.Message}");
        }
    }
}
