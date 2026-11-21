using System.Text.Json;
using System.Text.Json.Serialization;
using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;

namespace Shinano.Tool.Plugins;

/// <summary>
/// 随包原生插件的安装与升级。插件默认不启用（启用状态在数据库中，新插件没有记录即为停用），工具不改动启用状态。
/// 调用时宿主不在运行，可以直接改动插件目录、暂存区与重启计划。
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

    /// <summary>暂存区根目录。</summary>
    public static string StagingDirectory(string dataDirectory) => Path.Combine(dataDirectory, STAGING_DIRECTORY);

    /// <summary>首次初始化：未安装的原生插件装入插件目录；已安装的只记录。</summary>
    public static void InstallInitial(IReadOnlyList<BundledPlugin> bundled, string pluginRoot, DeploymentState state, ToolLog log)
    {
        IReadOnlyDictionary<string, InstalledPlugin> installed = PluginPackages.ScanInstalled(pluginRoot);
        foreach (BundledPlugin plugin in bundled)
        {
            if (!installed.ContainsKey(plugin.Manifest.Id) && Install(plugin, pluginRoot, log) is null) continue;
            Remember(state, plugin.Manifest.Id);
        }
    }

    /// <summary>
    /// 升级前校验：检查是否存在未完成的重启计划以及待处理变更文件是否损坏，在停服务和替换程序目录前拦截。
    /// </summary>
    /// <exception cref="ToolException">存在未完成的重启计划或待处理变更文件损坏；消息附带可操作建议。</exception>
    public static void ValidateBeforeUpgrade(string dataDirectory)
    {
        if (RestartPlanFile.Exists(dataDirectory))
            throw new ToolException(
                $"检测到未完成的插件重启计划 {RestartPlanFile.PlanPath(dataDirectory)}（可能正在等待应用或回滚）。"
                + "请先启动一次当前部署完成应用或回滚（服务模式运行 scripts/service start，直接运行模式运行 scripts/start），"
                + "确认宿主启动完成并正常停止后再重新运行 upgrade");

        string pendingPath = Path.Combine(StagingDirectory(dataDirectory), PENDING_FILE);
        if (File.Exists(pendingPath)) _ = ReadPending(pendingPath, Path.Combine(StagingDirectory(dataDirectory), PACKAGES_NAME));
    }

    /// <summary>
    /// 升级：已安装且新包版本更高的暂存并写入只含本次随包升级项的 <c>restart/plan.json</c>（首次启动由宿主自动应用并验证；
    /// 同一插件已有手动暂存时跳过并告警）；从未装入过的新插件直接装入；装入过但已不在插件目录中的视为管理员已删除，不再装回。
    /// </summary>
    /// <param name="bundled">新发布包中的原生插件。</param>
    /// <param name="pluginRoot">目标部署的插件目录。</param>
    /// <param name="dataDirectory">目标部署的数据目录。</param>
    /// <param name="state">目标部署的状态，装入的插件记入其中。</param>
    /// <param name="log">输出。</param>
    /// <param name="now">暂存与计划时间。</param>
    public static BundledUpgradeResult Upgrade(
        IReadOnlyList<BundledPlugin> bundled,
        string                       pluginRoot,
        string                       dataDirectory,
        DeploymentState              state,
        ToolLog                      log,
        DateTimeOffset               now)
    {
        ValidateBeforeUpgrade(dataDirectory);

        IReadOnlyDictionary<string, InstalledPlugin> installed = PluginPackages.ScanInstalled(pluginRoot);
        string stagingRoot = StagingDirectory(dataDirectory);
        string packagesRoot = Path.Combine(stagingRoot, PACKAGES_NAME);
        string pendingPath = Path.Combine(stagingRoot, PENDING_FILE);
        List<StagedPluginChange> pending = File.Exists(pendingPath) ? ReadPending(pendingPath, packagesRoot) : [];
        int initialPendingCount = pending.Count;

        List<string> installedDirectories = [];
        List<ToolPluginPackageOperation> operations = [];
        List<string> skippedManual = [];

        foreach (BundledPlugin plugin in bundled)
        {
            PluginManifest manifest = plugin.Manifest;
            if (!installed.TryGetValue(manifest.Id, out InstalledPlugin? current))
            {
                if (state.BundledPlugins.Contains(manifest.Id))
                {
                    log.Say($"  随包插件 {manifest.DisplayName} 已被删除，不再装回");
                    continue;
                }

                if (Install(plugin, pluginRoot, log) is { } created)
                {
                    installedDirectories.Add(created);
                    Remember(state, manifest.Id);
                }

                continue;
            }

            Remember(state, manifest.Id);
            switch (PluginPackages.IsNewer(manifest.Version, current.Manifest.Version))
            {
                case null:
                    log.Warning(
                        $"无法按语义化版本比较随包插件 {manifest.DisplayName}（当前 {current.Manifest.Version}，新包 {manifest.Version}），"
                        + $"已跳过升级；请检查 {Path.Combine(current.Directory, PluginManifest.FILE_NAME)} 与新包中的 version 字段");
                    continue;
                case false:
                    continue;
            }

            if (pending.Any(item => item.Change.PluginId == manifest.Id))
            {
                skippedManual.Add(manifest.Id);
                log.Warning(
                    $"随包插件 {manifest.DisplayName} 在面板已有待处理的手动变更，本次升级已跳过 {manifest.Version}。"
                    + $"升级完成后，请登录管理面板「插件管理」先应用或撤销该暂存，再决定是否手动上传 bundled-plugins/{manifest.Id}.zip");
                continue;
            }

            string target = Path.Combine(packagesRoot, manifest.Id);
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
            PluginPackages.Extract(plugin.ArchivePath, target);

            StagedChangeDetail detail = new()
            {
                Kind        = StagedChangeKind.Upgrade,
                PluginId    = manifest.Id,
                Name        = manifest.DisplayName,
                FromVersion = current.Manifest.Version,
                ToVersion   = manifest.Version
            };
            pending.Add(
                new StagedPluginChange
                {
                    Change    = detail,
                    StagedAt  = now,
                    SizeBytes = new FileInfo(plugin.ArchivePath).Length
                });
            operations.Add(
                new ToolPluginPackageOperation
                {
                    Change          = detail,
                    StagedDirectory = target,
                    TargetDirectory = current.Directory
                });
            log.Info($"已准备随包插件升级 {manifest.DisplayName} {current.Manifest.Version} → {manifest.Version}，宿主首次启动时将自动应用并验证");
        }

        DateTimeOffset? planCreatedAt = null;
        if (operations.Count > 0)
        {
            Directory.CreateDirectory(stagingRoot);
            string temporary = pendingPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(pending, StagingJson));
            File.Move(temporary, pendingPath, overwrite: true);

            RestartPlanFile.WritePlan(
                dataDirectory,
                new ToolRestartPlan
                {
                    CreatedAt  = now,
                    State      = ToolRestartPlanState.Pending,
                    Operations = operations
                });
            planCreatedAt = now;
            log.Info($"已为 {operations.Count} 个随包插件升级生成重启计划，宿主下次启动时将自动应用并在失败时回滚");
        }

        if (initialPendingCount > 0)
            log.Say($"提示：另有 {initialPendingCount} 项面板手动暂存的插件变更未随本次升级应用，请稍后在管理面板确认并重启生效。");

        return new BundledUpgradeResult
        {
            InstalledDirectories = installedDirectories,
            PlannedUpgrades      = [.. operations.Select(operation => operation.Change)],
            SkippedManualStaging = skippedManual,
            OtherPendingCount    = initialPendingCount,
            PlanCreatedAt        = planCreatedAt
        };
    }

    private static List<StagedPluginChange> ReadPending(string path, string packagesRoot)
    {
        try
        {
            return JsonSerializer.Deserialize<List<StagedPluginChange>>(File.ReadAllText(path), StagingJson) ?? [];
        }
        catch (JsonException exception)
        {
            throw new ToolException(
                $"待处理插件变更文件 {path} 无法解析：{exception.Message}。"
                + $"请检查或手动删除该文件及 {packagesRoot} 中对应残留目录后，重新运行 upgrade");
        }
    }

    private static string? Install(BundledPlugin plugin, string pluginRoot, ToolLog log)
    {
        string target = Path.Combine(pluginRoot, plugin.Manifest.Id);
        if (Directory.Exists(target))
        {
            log.Warning($"插件目录下已有 {plugin.Manifest.Id}，但不是该插件的插件包，未装入随包插件 {plugin.Manifest.DisplayName}");
            return null;
        }

        PluginPackages.Extract(plugin.ArchivePath, target);
        log.Info($"已装入随包插件 {plugin.Manifest.DisplayName} {plugin.Manifest.Version}（默认不启用，可在面板启用）");
        return target;
    }

    private static void Remember(DeploymentState state, string pluginId)
    {
        if (!state.BundledPlugins.Contains(pluginId)) state.BundledPlugins.Add(pluginId);
    }
}

/// <summary>一次升级中随包插件同步的摘要结果。</summary>
public sealed class BundledUpgradeResult
{
    /// <summary>本次新装入的插件目录。</summary>
    public required IReadOnlyList<string> InstalledDirectories { get; init; }

    /// <summary>本次写入重启计划、等待宿主首次启动应用的随包插件升级。</summary>
    public required IReadOnlyList<StagedChangeDetail> PlannedUpgrades { get; init; }

    /// <summary>因面板已有手动暂存而被跳过的随包插件 ID。</summary>
    public required IReadOnlyList<string> SkippedManualStaging { get; init; }

    /// <summary>升级前就存在、本次保持未动的面板手动暂存条目数。</summary>
    public required int OtherPendingCount { get; init; }

    /// <summary>本次生成重启计划的时间（UTC）；没有生成计划时为 null。</summary>
    public DateTimeOffset? PlanCreatedAt { get; init; }
}
