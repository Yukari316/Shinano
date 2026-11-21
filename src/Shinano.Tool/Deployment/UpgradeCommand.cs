using System.ComponentModel;
using System.Diagnostics;
using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Plugins;
using Shinano.Tool.Run;
using Shinano.Tool.Services;

namespace Shinano.Tool.Deployment;

/// <summary>
/// <c>upgrade --target &lt;部署根&gt;</c>：由新发布包中的工具对目标部署执行，避免替换正在运行的自身。服务模式下取得权限后停止服务、
/// 替换、再启动并等待就绪；直接运行模式要求进程已停止。数据库不回退，升级前提示备份；不询问配置，新增配置项取默认值。
/// </summary>
public static class UpgradeCommand
{
    /// <summary>该命令接受的选项。</summary>
    public static readonly IReadOnlyCollection<string> Options =
    [
        TARGET_OPTION,
        Prompter.YES_SWITCH,
        ToolContext.ACCEPT_DEFAULTS_SWITCH,
        ToolContext.ELEVATED_SWITCH,
        ToolContext.ELEVATED_OUTPUT_OPTION
    ];

    private const string TARGET_OPTION   = "target";
    private const char   METADATA_PREFIX = '+';
    private const string UNKNOWN_VERSION = "未知";
    private const string ROOT_USER       = "root";

    // 一次性迁移：1.0.0 及更早版本的部署配置文件名。全部部署都升级到改名之后的版本时，删除此常量及其迁移逻辑。
    private const string LEGACY_CONFIG_FILE_NAME = "shinano.local.json";

    private static readonly TimeSpan StateTimeout   = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(1);

    /// <summary>执行升级。</summary>
    public static async Task<int> RunAsync(ToolContext context)
    {
        DeploymentLayout source = context.Layout;
        string targetRoot = context.Arguments.Value(TARGET_OPTION) ?? throw new ToolException("用法：upgrade --target <要升级的部署根目录>");
        DeploymentLayout target = DeploymentLayout.At(targetRoot);
        if (DeploymentLayout.SamePath(source.Root, target.Root)) throw new ToolException("请把新发布包解压到另一个目录，再从那里对现有部署运行 upgrade");
        string legacyConfig = LegacyConfigPath(target);
        if (!File.Exists(target.HostExecutable) || !File.Exists(target.ConfigPath) && !File.Exists(legacyConfig))
            throw new ToolException($"{target.Root} 不是已初始化的 Shinano 部署");
        if (File.Exists(target.ConfigPath) && File.Exists(legacyConfig))
            throw new ToolException(
                $"{target.DataDirectory} 中同时存在 {DeploymentLayout.CONFIG_FILE_NAME} 与旧文件名 {LEGACY_CONFIG_FILE_NAME}，"
                + "请确认保留哪一份并删除另一份后重新运行 upgrade");
        DeploymentState state = DeploymentStateStore.Read(target)
                                ?? throw new ToolException($"{target.Root} 缺少部署状态 {target.StatePath}，请先在该部署运行 init");

        ToolLog log = new(target.ToolLogPath, context.Arguments.Value(ToolContext.ELEVATED_OUTPUT_OPTION));
        string fromVersion = VersionOf(target.HostAssembly);
        string toVersion = VersionOf(source.HostAssembly);
        bool service = state.RunMode == RunMode.Service;

        CheckVersionDirection(fromVersion, toVersion, target, context, service, log);

        // 停服务与替换程序目录前做完前置校验：配置损坏、重启计划残留、暂存清单损坏或随包插件损坏时不做任何改动就退出。
        _ = DeploymentConfig.Load(File.Exists(target.ConfigPath) ? target.ConfigPath : legacyConfig);
        BundledPluginSync.ValidateBeforeUpgrade(target.DataDirectory);
        IReadOnlyList<BundledPlugin> bundled = PluginPackages.ReadBundled(source.BundledPluginsDirectory);

        Upgrade upgrade = new()
        {
            Source      = source,
            Target      = target,
            State       = state,
            Bundled     = bundled,
            Log         = log,
            FromVersion = fromVersion,
            ToVersion   = toVersion
        };
        if (service) return await UpgradeServiceAsync(context, upgrade);

        if (IsRunning(target.HostExecutable) || IsRunning(target.MonitorExecutable) || IsRunning(target.ToolExecutable))
            throw new ToolException("目标部署的宿主、监控或运行守护进程仍在运行，请先在其控制台按 Ctrl+C 停止，再运行 upgrade");
        (string? failure, _) = Apply(upgrade, chownTo: null);
        if (failure is not null) throw new ToolException(failure);
        log.Info($"升级完成：{fromVersion} → {toVersion}");

        if (!context.Prompter.Interactive || !context.Prompter.Confirm("是否立即启动", null, fallback: true))
        {
            log.Say($"运行 {Path.Combine(target.Root, "scripts")} 下的 start 启动 Shinano。");
            return 0;
        }

        ProcessStartInfo info = new(target.ToolExecutable) { UseShellExecute = false };
        info.ArgumentList.Add("run");
        using Process run = Process.Start(info) ?? throw new ToolException($"无法启动 {target.ToolExecutable}");
        await run.WaitForExitAsync();
        return run.ExitCode;
    }

    /// <summary>
    /// 校验主程序版本变更方向：严格降级一律拒绝；同版本在交互模式下默认否询问覆盖重装，非交互模式允许重装。
    /// </summary>
    public static void CheckVersionDirection(
        string           fromVersion,
        string           toVersion,
        DeploymentLayout target,
        ToolContext      context,
        bool             service,
        ToolLog          log)
    {
        int? comparison = SemanticVersion.Compare(toVersion, fromVersion);
        if (comparison < 0)
            throw new ToolException(
                $"不支持从 {fromVersion} 降级到 {toVersion}（数据库结构迁移不可回退）。"
                + $"如需回退，请先恢复数据库备份，再手动将 {target.PreviousDirectory} 中的旧版移回 {target.Root}");

        if (context.Elevated) return;

        log.Say($"升级 {target.Root}：{fromVersion} → {toVersion}（{(service ? "服务模式" : "直接运行")}）");
        log.Say("新版本可能执行数据库结构迁移；迁移不会回退，回退程序后旧版本不保证可用。建议先用 pg_dump 备份数据库。");

        if (comparison == 0)
        {
            if (context.Prompter.Interactive && !context.Arguments.Switch(Prompter.YES_SWITCH))
            {
                if (!context.Prompter.Confirm($"目标部署已是相同版本 {toVersion}，是否覆盖重装", null, fallback: false))
                    throw new ToolException("已取消");
                return;
            }

            log.Say($"目标部署已是相同版本 {toVersion}，将执行覆盖重装。");
            return;
        }

        if (comparison is null)
            log.Warning($"无法按语义化版本比较 {fromVersion} 与 {toVersion}，请确认不是降级操作");

        context.Prompter.RequireConfirmation("继续升级");
    }

    /// <summary>
    /// 服务模式重启并就绪后，核对本次随包插件重启计划的执行结果。
    /// </summary>
    /// <returns>是否全部通过验证；发生回滚、未验证或结果缺失时为 false。</returns>
    public static bool ReportRestartOutcome(string dataDirectory, DateTimeOffset planCreatedAt, ToolLog log)
    {
        if (RestartPlanFile.Exists(dataDirectory))
        {
            log.Warning("随包插件升级的重启计划尚未被宿主消费，请查看 host/Logs 并登录管理面板「插件管理」确认插件状态");
            return false;
        }

        ToolRestartResult? result;
        try
        {
            result = RestartPlanFile.ReadResult(dataDirectory);
        }
        catch (ToolException exception)
        {
            log.Warning($"{exception.Message}；请查看 host/Logs 并登录管理面板「插件管理」确认插件状态");
            return false;
        }

        if (result is null || result.FinishedAt < planCreatedAt - ClockTolerance)
        {
            log.Warning("未读取到本次随包插件升级的验证结果，请查看 host/Logs 并登录管理面板「插件管理」核对插件版本");
            return false;
        }

        string summary = DescribeChanges(result.Changes);
        switch (result.Outcome)
        {
            case ToolRestartOutcome.Succeeded:
                log.Info($"随包插件升级已通过宿主加载验证并生效（{summary}）");
                return true;
            case ToolRestartOutcome.RolledBack:
                log.Warning(
                    $"随包插件升级未通过加载验证，宿主已自动还原上一套插件包（{summary}）；原因：{result.Reason ?? "未知"}。"
                    + "请查看 host/Logs 排查错误，并登录管理面板「插件管理」确认回滚结果");
                return false;
            case ToolRestartOutcome.Unverified:
                log.Warning(
                    $"基础设施处于故障模式，随包插件已应用但未经加载验证（{summary}）；原因：{result.Reason ?? "未知"}。"
                    + "请恢复数据库与 RustFS 后查看 host/Logs 并核对插件状态");
                return false;
            default:
                log.Warning($"随包插件升级返回未知结果 {result.Outcome}，请查看 host/Logs 与管理面板「插件管理」");
                return false;
        }
    }

    /// <summary>
    /// 核对服务模式状态并在系统服务实际已安装时补正 <see cref="DeploymentState.ServicesInstalled" />，
    /// 同时解析 Linux 下服务实际运行用户（用于精确恢复属主）。
    /// </summary>
    public static string? VerifyServiceAndResolveOwner(
        IServiceManager manager,
        ServiceSetup    setup,
        DeploymentState state,
        bool            isLinux,
        ToolLog         log)
    {
        ServiceCommand.Verify(manager, setup);
        if (!state.ServicesInstalled)
        {
            state.ServicesInstalled = true;
            log.Info("检测到系统服务已安装且指向本部署，已补正部署状态");
        }

        if (!isLinux) return null;
        string? owner = manager.Query(setup.Host.Name).User ?? state.ServiceUser;
        return string.IsNullOrWhiteSpace(owner) || owner.Equals(ROOT_USER, StringComparison.Ordinal) ? null : owner;
    }

    private static async Task<int> UpgradeServiceAsync(ToolContext context, Upgrade upgrade)
    {
        DeploymentLayout target = upgrade.Target;
        ToolLog log = upgrade.Log;
        IServiceManager manager = ServiceManagers.ForCurrentPlatform();
        ServiceSetup setup = ServiceManagers.Setup(target, upgrade.State);
        string? owner = VerifyServiceAndResolveOwner(manager, setup, upgrade.State, OperatingSystem.IsLinux(), log);

        if (!Privilege.IsPrivileged)
        {
            if (context.Elevated) throw new ToolException("提权后的进程仍然没有管理员权限");
            return Privilege.RunElevated(context, ["upgrade", "--" + TARGET_OPTION, target.Root, "--" + Prompter.YES_SWITCH])
                   ?? throw new ToolException("服务模式升级需要管理员权限：请在管理员终端中（Linux 用 sudo）重新运行 upgrade");
        }

        try
        {
            foreach (SystemCommand command in manager.StopCommands(setup))
                CommandRunner.Run(command, log);
            foreach (ServiceDefinition service in setup.Services)
                if (!await manager.WaitAsync(service.Name, running: false, StateTimeout))
                    throw new ToolException($"服务 {service.Name} 未在 {StateTimeout.TotalSeconds} 秒内停止");
        }
        catch (ToolException exception)
        {
            // 可能已有服务停下：重新启动，不让 Bot 停在半停止状态。
            string restart = StartServices(manager, setup, log) is { } error ? $"，重新启动服务也失败：{error}" : "，已重新启动服务";
            throw new ToolException($"{exception.Message}；未做任何替换{restart}。请检查后重新运行 upgrade");
        }

        string? failure = null;
        BundledUpgradeResult? bundledResult = null;
        string? startFailure;
        try
        {
            (failure, bundledResult) = Apply(upgrade, chownTo: owner);
        }
        finally
        {
            // 无论替换与同步结果如何都启动服务：替换失败时运行的是已还原的旧版。
            startFailure = StartServices(manager, setup, log);
        }

        string outcome = failure ?? $"升级完成：{upgrade.FromVersion} → {upgrade.ToVersion}";
        if (startFailure is not null) throw new ToolException($"{outcome}；但服务启动失败：{startFailure}");

        DeploymentConfig config = DeploymentConfig.Load(target.ConfigPath);
        using ReadinessProbe probe = new(HostEndpoint.Resolve(config, ConfigKeys.WEB, ConfigKeys.DEFAULT_PANEL_PORT, target.DataDirectory));
        bool ready = await probe.WaitReadyAsync(null, CancellationToken.None);

        if (failure is not null) throw new ToolException($"{failure}；服务已重新启动{(ready ? "，宿主已就绪" : "，但宿主未就绪，请查看 host/Logs")}");
        if (!ready)
        {
            log.Warning($"{outcome}，但宿主未在 {ReadinessProbe.ReadyTimeout.TotalSeconds} 秒内就绪，请查看 host/Logs");
            return 1;
        }

        log.Info($"{outcome}，宿主已就绪");
        if (bundledResult?.PlanCreatedAt is { } planCreatedAt
            && !ReportRestartOutcome(target.DataDirectory, planCreatedAt, log))
            return 1;

        return 0;
    }

    // 返回失败说明（成功时为 null）与随包插件同步结果。替换失败时 ProgramSwap 已还原旧版；替换成功后的失败发生在新版本装好之后，必须如实报告。
    private static (string? Failure, BundledUpgradeResult? BundledResult) Apply(Upgrade upgrade, string? chownTo)
    {
        DeploymentLayout target = upgrade.Target;
        try
        {
            ProgramSwap.Replace(upgrade.Source, target, upgrade.Log);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ($"升级失败，已还原 {upgrade.FromVersion}：{exception.Message}", null);
        }
        catch (ToolException exception)
        {
            // 还原旧版也失败了，消息中写明旧版的位置与手动还原方法。
            return ($"升级失败，未能自动还原：{exception.Message}", null);
        }

        try
        {
            MigrateConfigFileName(target, upgrade.Log);
            DeploymentConfig config = DeploymentConfig.Load(target.ConfigPath);
            string pluginRoot = Path.GetFullPath(config.GetString(ConfigKeys.PLUGIN_DIRECTORY) ?? ConfigKeys.DEFAULT_PLUGIN_DIRECTORY, target.DataDirectory);
            BundledUpgradeResult result = BundledPluginSync.Upgrade(
                upgrade.Bundled,
                pluginRoot,
                target.DataDirectory,
                upgrade.State,
                upgrade.Log,
                DateTimeOffset.UtcNow);
            DeploymentStateStore.Write(target, upgrade.State);
            if (chownTo is not null) RestoreOwnership(target, pluginRoot, result, chownTo, upgrade.Log);
            return (null, result);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ToolException)
        {
            return ($"新版本 {upgrade.ToVersion} 已安装，后续步骤失败：{exception.Message}", null);
        }
    }

    // 只把本次替换的程序目录与升级触及的数据文件恢复为服务运行用户所有，不递归扫描 data/nb2、data/browser 等大目录。
    private static void RestoreOwnership(
        DeploymentLayout     target,
        string               pluginRoot,
        BundledUpgradeResult result,
        string               owner,
        ToolLog              log)
    {
        List<string> recursive =
        [
            .. DeploymentLayout.ProgramDirectoryNames.Select(name => Path.Combine(target.Root, name)).Where(Directory.Exists),
            .. (string[]) [target.PreviousDirectory],
            .. result.InstalledDirectories
        ];
        if (result.PlanCreatedAt is not null)
        {
            recursive.Add(BundledPluginSync.StagingDirectory(target.DataDirectory));
            string? restartDirectory = Path.GetDirectoryName(RestartPlanFile.PlanPath(target.DataDirectory));
            if (restartDirectory is not null) recursive.Add(restartDirectory);
        }

        string[] existingRecursive = [.. recursive.Where(path => Directory.Exists(path) || File.Exists(path)).Distinct(StringComparer.Ordinal)];
        if (existingRecursive.Length > 0)
            CommandRunner.Run(
                new SystemCommand
                {
                    FileName    = "chown",
                    Arguments   = ["-R", owner + ":", .. existingRecursive],
                    Description = $"把升级后的程序与变更目录改为 {owner} 所有"
                },
                log);

        List<string> shallow = [target.DataDirectory, target.ConfigPath, target.StatePath, target.ToolLogPath];
        if (result.InstalledDirectories.Count > 0) shallow.Add(pluginRoot);
        string[] existingShallow = [.. shallow.Where(path => Directory.Exists(path) || File.Exists(path)).Distinct(StringComparer.Ordinal)];
        if (existingShallow.Length > 0)
            CommandRunner.Run(
                new SystemCommand
                {
                    FileName    = "chown",
                    Arguments   = [owner + ":", .. existingShallow],
                    Description = $"把升级触及的数据文件改为 {owner} 所有"
                },
                log);
    }

    // 在新版程序装好之后改名：替换失败还原旧版时，旧版仍按旧文件名读取。目标已存在时 File.Move 抛出 IOException，不覆盖。
    private static void MigrateConfigFileName(DeploymentLayout target, ToolLog log)
    {
        string legacy = LegacyConfigPath(target);
        if (!File.Exists(legacy)) return;
        File.Move(legacy, target.ConfigPath);
        log.Info($"部署配置文件已改名：{LEGACY_CONFIG_FILE_NAME} → {DeploymentLayout.CONFIG_FILE_NAME}");
    }

    private static string LegacyConfigPath(DeploymentLayout target) => Path.Combine(target.DataDirectory, LEGACY_CONFIG_FILE_NAME);

    // 返回失败说明，成功时为 null。
    private static string? StartServices(IServiceManager manager, ServiceSetup setup, ToolLog log)
    {
        try
        {
            foreach (SystemCommand command in manager.StartCommands(setup))
                CommandRunner.Run(command, log);
            return null;
        }
        catch (ToolException exception)
        {
            log.Error($"启动服务失败：{exception.Message}");
            return exception.Message;
        }
    }

    private static string VersionOf(string assembly)
    {
        if (!File.Exists(assembly)) return UNKNOWN_VERSION;
        string? version = FileVersionInfo.GetVersionInfo(assembly).ProductVersion;
        return version is null ? UNKNOWN_VERSION : version.Split(METADATA_PREFIX)[0];
    }

    private static string DescribeChanges(IReadOnlyList<StagedChangeDetail> changes) =>
        changes.Count == 0
            ? "无详情"
            : string.Join("、", changes.Select(change => $"{change.Name} {change.FromVersion} → {change.ToVersion}"));

    private static bool IsRunning(string executable)
    {
        int currentProcessId = Environment.ProcessId;
        return Process.GetProcesses().Any(process =>
        {
            using (process)
            {
                if (process.Id == currentProcessId) return false;
                try
                {
                    return process.MainModule?.FileName is { } path && DeploymentLayout.SamePath(path, executable);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // 其他用户或已退出的进程无法读取路径；目标部署的直接运行进程由当前用户运行。
                    return false;
                }
            }
        });
    }

    /// <summary>一次升级的输入。</summary>
    private sealed class Upgrade
    {
        public required DeploymentLayout             Source      { get; init; }
        public required DeploymentLayout             Target      { get; init; }
        public required DeploymentState              State       { get; init; }
        public required IReadOnlyList<BundledPlugin> Bundled     { get; init; }
        public required ToolLog                      Log         { get; init; }
        public required string                       FromVersion { get; init; }
        public required string                       ToVersion   { get; init; }
    }
}
