using System.ComponentModel;
using System.Diagnostics;
using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Deployment;
using Shinano.Tool.Deployment.Models;
using Shinano.Tool.Plugins;
using Shinano.Tool.Run;
using Shinano.Tool.Services;
using Shinano.Tool.Services.Models;

namespace Shinano.Tool.Upgrade;

/// <summary>
/// <c>upgrade --target &lt;部署根&gt;</c>：由新发布包中的工具对目标部署执行，避免替换正在运行的自身。服务模式下取得权限后停止服务、
/// 替换、再启动并等待就绪；直接运行模式要求进程已停止。数据库不回退，升级前提示备份；不询问配置，新增配置项取默认值。
/// </summary>
public static class UpgradeCommand
{
    /// <summary>该命令接受的选项。</summary>
    public static readonly IReadOnlyCollection<string> Options =
        [TARGET_OPTION, Prompter.YES_SWITCH, ToolContext.ELEVATED_SWITCH, ToolContext.ELEVATED_OUTPUT_OPTION];

    private const string TARGET_OPTION    = "target";
    private const char   METADATA_PREFIX  = '+';
    private const string UNKNOWN_VERSION  = "未知";

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(60);

    /// <summary>执行升级。</summary>
    public static async Task<int> RunAsync(ToolContext context)
    {
        DeploymentLayout source = context.Layout;
        string targetRoot = context.Arguments.Value(TARGET_OPTION) ?? throw new ToolException("用法：upgrade --target <要升级的部署根目录>");
        DeploymentLayout target = DeploymentLayout.At(targetRoot);
        if (SamePath(source.Root, target.Root)) throw new ToolException("请把新发布包解压到另一个目录，再从那里对现有部署运行 upgrade");
        if (!File.Exists(target.HostExecutable) || !File.Exists(target.LocalConfigPath))
            throw new ToolException($"{target.Root} 不是已初始化的 Shinano 部署");
        DeploymentState state = DeploymentStateStore.Read(target)
                                ?? throw new ToolException($"{target.Root} 缺少部署状态 {target.StatePath}，请先在该部署运行 init");

        ToolLog log = new(target.ToolLogPath, context.Arguments.Value(ToolContext.ELEVATED_OUTPUT_OPTION));
        string fromVersion = VersionOf(target.HostAssembly);
        string toVersion = VersionOf(source.HostAssembly);
        bool service = state is { RunMode: RunMode.Service, ServicesInstalled: true };
        if (!context.Elevated)
        {
            log.Say($"升级 {target.Root}：{fromVersion} → {toVersion}（{(service ? "服务模式" : "直接运行")}）");
            log.Say("新版本可能执行数据库结构迁移；迁移不会回退，回退程序后旧版本不保证可用。建议先用 pg_dump 备份数据库。");
            context.Prompter.RequireConfirmation("继续升级");
        }

        if (service) return await UpgradeServiceAsync(context, source, target, state, log, fromVersion, toVersion);

        if (IsRunning(target.HostExecutable) || IsRunning(target.MonitorExecutable))
            throw new ToolException("目标部署的宿主或监控仍在运行，请先在其控制台按 Ctrl+C 停止，再运行 upgrade");
        Apply(source, target, state, log);
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

    private static async Task<int> UpgradeServiceAsync(
        ToolContext      context,
        DeploymentLayout source,
        DeploymentLayout target,
        DeploymentState  state,
        ToolLog          log,
        string           fromVersion,
        string           toVersion)
    {
        IServiceManager manager = ServiceManagers.ForCurrentPlatform();
        ServiceSetup setup = ServiceManagers.Setup(target, state);
        ServiceCommand.Verify(manager, setup);
        if (!Privilege.IsPrivileged)
        {
            if (context.Elevated) throw new ToolException("提权后的进程仍然没有管理员权限");
            return Privilege.RunElevated(context, ["upgrade", "--" + TARGET_OPTION, target.Root, "--" + Prompter.YES_SWITCH])
                   ?? throw new ToolException("服务模式升级需要管理员权限：请在管理员终端中（Linux 用 sudo）重新运行 upgrade");
        }

        foreach (SystemCommand command in manager.StopCommands(setup))
            CommandRunner.Run(command, log);
        foreach (ServiceDefinition service in setup.Services)
            if (!await manager.WaitAsync(service.Name, running: false, StateTimeout))
                throw new ToolException($"服务 {service.Name} 未在 {StateTimeout.TotalSeconds} 秒内停止，未做任何替换；请检查后重新运行 upgrade");

        Exception? failure = null;
        try
        {
            Apply(source, target, state, log);
            if (OperatingSystem.IsLinux() && state.ServiceUser is { } user)
                CommandRunner.Run(
                    new SystemCommand
                    {
                        FileName    = "chown",
                        Arguments   = ["-R", user + ":", target.Root],
                        Description = $"把部署目录改为 {user} 所有"
                    },
                    log);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ToolException)
        {
            // 替换失败时已还原旧版；仍然启动服务，不让 Bot 停在半升级状态。
            failure = exception;
        }

        foreach (SystemCommand command in manager.StartCommands(setup))
            CommandRunner.Run(command, log);
        LocalConfig config = LocalConfig.Load(target.LocalConfigPath);
        using ReadinessProbe probe = new(HostEndpoint.Resolve(config, ConfigKeys.WEB, ConfigKeys.DEFAULT_PANEL_PORT, target.DataDirectory));
        bool ready = await probe.WaitReadyAsync(null, CancellationToken.None);

        if (failure is not null) throw new ToolException($"升级失败，已还原并重新启动 {fromVersion}：{failure.Message}");
        if (ready) log.Info($"升级完成：{fromVersion} → {toVersion}，宿主已就绪");
        else log.Warning($"升级完成：{fromVersion} → {toVersion}，但宿主未在 {ReadinessProbe.ReadyTimeout.TotalSeconds} 秒内就绪，请查看 host/Logs");
        return ready ? 0 : 1;
    }

    private static void Apply(DeploymentLayout source, DeploymentLayout target, DeploymentState state, ToolLog log)
    {
        ProgramSwap.Replace(source, target, log);
        LocalConfig config = LocalConfig.Load(target.LocalConfigPath);
        string pluginRoot = Path.GetFullPath(config.GetString(ConfigKeys.PLUGIN_DIRECTORY) ?? ConfigKeys.DEFAULT_PLUGIN_DIRECTORY, target.DataDirectory);
        BundledPluginSync.Upgrade(
            PluginPackages.ReadBundled(source.BundledPluginsDirectory),
            pluginRoot,
            target.DataDirectory,
            state,
            log,
            DateTimeOffset.UtcNow);
        DeploymentStateStore.Write(target, state);
    }

    private static string VersionOf(string assembly)
    {
        if (!File.Exists(assembly)) return UNKNOWN_VERSION;
        string? version = FileVersionInfo.GetVersionInfo(assembly).ProductVersion;
        return version is null ? UNKNOWN_VERSION : version.Split(METADATA_PREFIX)[0];
    }

    private static bool IsRunning(string executable) =>
        Process.GetProcesses().Any(process =>
        {
            using (process)
            {
                try
                {
                    return process.MainModule?.FileName is { } path && SamePath(path, executable);
                }
                catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // 其他用户或已退出的进程无法读取路径；目标部署的进程由当前用户运行。
                    return false;
                }
            }
        });

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
