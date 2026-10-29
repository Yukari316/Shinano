using System.Text.Json;
using System.Text.Json.Serialization;
using Shinano.Tool.Deployment;
using Shinano.Tool.Deployment.Models;
using Shinano.Tool.Plugins.Models;

namespace Shinano.Tool.Plugins;

/// <summary>
/// 随包原生插件的安装与升级。插件默认不启用（启用状态在数据库中，新插件没有记录即为停用），工具不改动启用状态。
/// 调用时宿主不在运行，可以直接改动插件目录与暂存区。
/// </summary>
public static class BundledPluginSync
{
    private const string STAGING_DIRECTORY = "plugin-staging";
    private const string PACKAGES_NAME     = "packages";
    private const string PENDING_FILE      = "pending.json";

    // 与宿主暂存区状态文件的序列化方式一致。
    private static readonly JsonSerializerOptions StagingJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    /// <summary>首次初始化：未安装的原生插件装入插件目录；已安装的只记录。</summary>
    public static void InstallInitial(IReadOnlyList<BundledPlugin> bundled, string pluginRoot, DeploymentState state, ToolLog log)
    {
        IReadOnlyDictionary<string, InstalledPlugin> installed = PluginPackages.ScanInstalled(pluginRoot);
        foreach (BundledPlugin plugin in bundled)
        {
            if (!installed.ContainsKey(plugin.Manifest.Id) && !Install(plugin, pluginRoot, log)) continue;
            Remember(state, plugin.Manifest.Id);
        }
    }

    /// <summary>
    /// 升级：已安装且新包版本更高的暂存为待处理升级（同一插件已有暂存变更时跳过）；从未装入过的新插件直接装入；
    /// 装入过但已不在插件目录中的视为管理员已删除，不再装回。
    /// </summary>
    /// <param name="bundled">新发布包中的原生插件。</param>
    /// <param name="pluginRoot">目标部署的插件目录。</param>
    /// <param name="dataDirectory">目标部署的数据目录。</param>
    /// <param name="state">目标部署的状态，装入的插件记入其中。</param>
    /// <param name="log">输出。</param>
    /// <param name="now">暂存时间。</param>
    public static void Upgrade(
        IReadOnlyList<BundledPlugin> bundled,
        string                       pluginRoot,
        string                       dataDirectory,
        DeploymentState              state,
        ToolLog                      log,
        DateTimeOffset               now)
    {
        IReadOnlyDictionary<string, InstalledPlugin> installed = PluginPackages.ScanInstalled(pluginRoot);
        string stagingRoot = Path.Combine(dataDirectory, STAGING_DIRECTORY);
        string pendingPath = Path.Combine(stagingRoot, PENDING_FILE);
        List<StagedPluginChange> pending = File.Exists(pendingPath)
            ? JsonSerializer.Deserialize<List<StagedPluginChange>>(File.ReadAllText(pendingPath), StagingJson) ?? []
            : [];
        bool pendingChanged = false;

        foreach (BundledPlugin plugin in bundled)
        {
            PluginManifest manifest = plugin.Manifest;
            if (!installed.TryGetValue(manifest.Id, out InstalledPlugin? current))
            {
                if (state.BundledPlugins.Contains(manifest.Id))
                {
                    log.Say($"  原生插件 {manifest.DisplayName} 已被删除，不再装回");
                    continue;
                }

                if (Install(plugin, pluginRoot, log)) Remember(state, manifest.Id);
                continue;
            }

            Remember(state, manifest.Id);
            switch (PluginPackages.IsNewer(manifest.Version, current.Manifest.Version))
            {
                case null:
                    log.Warning($"无法比较原生插件 {manifest.DisplayName} 的版本（{current.Manifest.Version} 与 {manifest.Version}），未暂存升级");
                    continue;
                case false:
                    continue;
            }

            if (pending.Any(item => item.Change.PluginId == manifest.Id))
            {
                log.Warning($"原生插件 {manifest.DisplayName} 已有待处理的变更，未暂存 {manifest.Version} 的升级");
                continue;
            }

            string target = Path.Combine(stagingRoot, PACKAGES_NAME, manifest.Id);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            PluginPackages.Extract(plugin.ArchivePath, target);
            pending.Add(
                new StagedPluginChange
                {
                    Change = new StagedChangeDetail
                    {
                        Kind        = StagedChangeKind.Upgrade,
                        PluginId    = manifest.Id,
                        Name        = manifest.DisplayName,
                        FromVersion = current.Manifest.Version,
                        ToVersion   = manifest.Version
                    },
                    StagedAt  = now,
                    SizeBytes = new FileInfo(plugin.ArchivePath).Length
                });
            pendingChanged = true;
            log.Info($"已暂存原生插件升级 {manifest.DisplayName} {current.Manifest.Version} → {manifest.Version}，在面板重启后生效");
        }

        if (!pendingChanged) return;
        Directory.CreateDirectory(stagingRoot);
        string temporary = pendingPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(pending, StagingJson));
        File.Move(temporary, pendingPath, overwrite: true);
    }

    private static bool Install(BundledPlugin plugin, string pluginRoot, ToolLog log)
    {
        string target = Path.Combine(pluginRoot, plugin.Manifest.Id);
        if (Directory.Exists(target))
        {
            log.Warning($"插件目录下已有 {plugin.Manifest.Id}，但不是该插件的插件包，未装入原生插件 {plugin.Manifest.DisplayName}");
            return false;
        }

        PluginPackages.Extract(plugin.ArchivePath, target);
        log.Info($"已装入原生插件 {plugin.Manifest.DisplayName} {plugin.Manifest.Version}（默认不启用，可在面板启用）");
        return true;
    }

    private static void Remember(DeploymentState state, string pluginId)
    {
        if (!state.BundledPlugins.Contains(pluginId)) state.BundledPlugins.Add(pluginId);
    }
}
