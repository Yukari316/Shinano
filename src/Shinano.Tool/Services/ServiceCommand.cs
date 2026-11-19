using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Deployment;
using Shinano.Tool.Run;
using Shinano.Tool.Setup;

namespace Shinano.Tool.Services;

/// <summary>
/// <c>service &lt;install|uninstall|start|stop|status&gt;</c>：宿主与监控两个服务一起操作。修改操作先检查权限，没有权限时提权重新运行
/// 同一动作，无法提权时输出命令。部署状态只由最外层的进程写入，提权子进程只执行需要权限的那一步。
/// </summary>
public static class ServiceCommand
{
    /// <summary>该命令接受的选项。</summary>
    public static readonly IReadOnlyCollection<string> Options =
        [DELETE_USER_OPTION, ToolContext.ELEVATED_SWITCH, ToolContext.ELEVATED_OUTPUT_OPTION];

    private const string DELETE_USER_OPTION = "delete-user";
    private const string COMMAND            = "service";
    private const string INSTALL            = "install";
    private const string UNINSTALL          = "uninstall";
    private const string START              = "start";
    private const string STOP               = "stop";
    private const string STATUS             = "status";

    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(60);

    /// <summary>执行 <c>service</c> 子命令。</summary>
    public static async Task<int> RunAsync(ToolContext context)
    {
        string action = context.Arguments.Positionals.ElementAtOrDefault(1)
                        ?? throw new ToolException("用法：service <install|uninstall|start|stop|status>");
        DeploymentState state = DeploymentStateStore.Read(context.Layout) ?? throw new ToolException("尚未初始化，请先运行 init");
        IServiceManager manager = ServiceManagers.ForCurrentPlatform();
        ServiceSetup setup = ServiceManagers.Setup(context.Layout, state);
        return action switch
        {
            INSTALL   => await InstallAsync(context, state, manager) ? 0 : 2,
            UNINSTALL => await UninstallAsync(context, state, manager, setup, removingBot: true) ? 0 : 2,
            START     => await StartAsync(context, manager, setup) ? 0 : 2,
            STOP      => await StopAsync(context, manager, setup) ? 0 : 2,
            STATUS    => Status(context, state, manager, setup),
            _         => throw new ToolException($"未知动作 {action}，可用：install、uninstall、start、stop、status")
        };
    }

    /// <summary>创建并启动服务，完成后等待宿主就绪。</summary>
    /// <returns>是否已执行；无法取得权限、只输出了命令时为 false。</returns>
    public static async Task<bool> InstallAsync(ToolContext context, DeploymentState state, IServiceManager manager)
    {
        ServiceSetup setup = ServiceManagers.Setup(context.Layout, state);
        bool createsUser = OperatingSystem.IsLinux() && setup.DedicatedUser && !SystemdServiceManager.UserExists(setup.User);
        manager.Prepare(setup);
        IReadOnlyList<SystemCommand> commands = [.. manager.InstallCommands(setup), .. manager.StartCommands(setup)];
        bool executed = await ExecuteAsync(context, RunAllAsync(commands, context.Log), commands, [COMMAND, INSTALL]);
        if (context.Elevated) return executed;

        state.RunMode = RunMode.Service;
        state.ServicesInstalled = executed;
        state.ServiceUserCreated |= executed && createsUser;
        DeploymentStateStore.Write(context.Layout, state);
        if (!executed)
        {
            context.Log.Say("执行以上命令后，可运行 service status 核对服务状态。");
            return false;
        }

        await ReportReadyAsync(context);
        return true;
    }

    /// <summary>停止并删除服务；不删除部署目录、数据库与 bucket。</summary>
    /// <param name="context">上下文。</param>
    /// <param name="state">部署状态，删除后写回。</param>
    /// <param name="manager">服务管理器。</param>
    /// <param name="setup">要删除的服务。</param>
    /// <param name="removingBot">是否为删除 Bot 而卸载：为 true 时结束后列出仍保留的资源；切换运行方式时为 false。</param>
    /// <returns>是否已执行。</returns>
    public static async Task<bool> UninstallAsync(ToolContext context, DeploymentState state, IServiceManager manager, ServiceSetup setup, bool removingBot)
    {
        bool deleteUser = state.ServiceUserCreated
                          && context.Prompter.Confirm($"是否同时删除专用用户 {state.ServiceUser}", DELETE_USER_OPTION, fallback: false);
        IReadOnlyList<SystemCommand> stop = [.. manager.StopCommands(setup)];
        IReadOnlyList<SystemCommand> remove = manager.UninstallCommands(setup, deleteUser, deleteUser ? ServiceManagers.InvokingUser() : null);
        bool executed = await ExecuteAsync(
            context,
            async () =>
            {
                if (setup.Services.Any(service => manager.Query(service.Name).Running))
                {
                    RunAll(stop, context.Log);
                    await WaitAllAsync(manager, setup, running: false, context.Log);
                }

                RunAll(manager.UninstallCommands(setup, deleteUser, deleteUser ? ServiceManagers.InvokingUser() : null), context.Log);
            },
            [.. stop, .. remove],
            [COMMAND, UNINSTALL, $"--{DELETE_USER_OPTION}", deleteUser ? "yes" : "no"]);
        if (context.Elevated || !executed) return executed;

        state.RunMode = RunMode.Direct;
        state.ServicesInstalled = false;
        if (deleteUser)
        {
            state.ServiceUser = null;
            state.ServiceUserCreated = false;
        }

        DeploymentStateStore.Write(context.Layout, state);
        context.Log.Info("服务已删除");
        if (!removingBot) return true;

        LocalConfig config = LocalConfig.Load(context.Layout.LocalConfigPath);
        context.Log.Say("以下资源仍然保留，需要时请自行处理：");
        context.Log.Say($"  部署目录：{context.Layout.Root}");
        context.Log.Say($"  数据库：{DatabaseName(config) ?? "（未配置）"}（保留以供再次部署时使用）");
        context.Log.Say($"  bucket：{config.GetString(ConfigKeys.BUCKET_PREFIX) ?? "shinano"}-*");
        return true;
    }

    /// <summary>启动服务并等待宿主就绪。</summary>
    /// <returns>是否已执行。</returns>
    public static async Task<bool> StartAsync(ToolContext context, IServiceManager manager, ServiceSetup setup)
    {
        IReadOnlyList<SystemCommand> commands = manager.StartCommands(setup);
        bool executed = await ExecuteAsync(context, RunAllAsync(commands, context.Log), commands, [COMMAND, START]);
        if (executed && !context.Elevated) await ReportReadyAsync(context);
        return executed;
    }

    /// <summary>停止服务并等待两个进程退出。</summary>
    /// <returns>是否已执行。</returns>
    public static async Task<bool> StopAsync(ToolContext context, IServiceManager manager, ServiceSetup setup)
    {
        IReadOnlyList<SystemCommand> commands = manager.StopCommands(setup);
        return await ExecuteAsync(
            context,
            async () =>
            {
                RunAll(commands, context.Log);
                await WaitAllAsync(manager, setup, running: false, context.Log);
            },
            commands,
            [COMMAND, STOP]);
    }

    /// <summary>
    /// 核对服务存在且可执行文件指向这个部署。部署状态记录服务已创建、但服务管理器中的实际情况不一致时停止，不猜测。
    /// </summary>
    /// <exception cref="ToolException">服务不存在或指向其他部署。</exception>
    public static void Verify(IServiceManager manager, ServiceSetup setup)
    {
        foreach (ServiceDefinition service in setup.Services)
        {
            ServiceStatus status = manager.Query(service.Name);
            if (!status.Exists)
                throw new ToolException($"部署状态记录了服务 {service.Name}，但服务管理器中不存在；请运行 service install 重新创建，或重新运行 init");
            if (status.Executable is null || !SamePath(status.Executable, service.Executable))
                throw new ToolException($"服务 {service.Name} 指向 {status.Executable ?? "未知位置"}，不是这个部署（{service.Executable}）；请核对后再操作");
        }
    }

    // 有权限时直接执行；否则提权重新运行同一动作；都不行时输出命令，返回 false。
    private static async Task<bool> ExecuteAsync(
        ToolContext                  context,
        Func<Task>                   privileged,
        IReadOnlyList<SystemCommand> rendered,
        IReadOnlyList<string>        relaunch)
    {
        if (Privilege.IsPrivileged)
        {
            await privileged();
            return true;
        }

        if (context.Elevated) throw new ToolException("提权后的进程仍然没有管理员权限");
        switch (Privilege.RunElevated(context, relaunch))
        {
            case 0:
                return true;
            case { } exitCode:
                throw new ToolException($"以管理员身份执行失败（退出码 {exitCode}），详见上方输出与 tool.log");
        }

        context.Log.Warning("没有管理员权限，未执行服务操作");
        context.Log.Say(OperatingSystem.IsWindows()
                            ? "请以管理员身份打开命令提示符（cmd），依次执行："
                            : "请以 root 身份（如加 sudo）依次执行：");
        foreach (SystemCommand command in rendered)
            context.Log.Say("  " + (OperatingSystem.IsWindows() ? "" : "sudo ") + CommandRunner.Render(command));
        return false;
    }

    private static void RunAll(IReadOnlyList<SystemCommand> commands, ToolLog log)
    {
        foreach (SystemCommand command in commands)
            CommandRunner.Run(command, log);
    }

    private static Func<Task> RunAllAsync(IReadOnlyList<SystemCommand> commands, ToolLog log) =>
        () =>
        {
            RunAll(commands, log);
            return Task.CompletedTask;
        };

    private static async Task WaitAllAsync(IServiceManager manager, ServiceSetup setup, bool running, ToolLog log)
    {
        foreach (ServiceDefinition service in setup.Services)
            if (!await manager.WaitAsync(service.Name, running, StateTimeout))
                throw new ToolException($"服务 {service.Name} 未在 {StateTimeout.TotalSeconds} 秒内{(running ? "启动" : "停止")}");
        log.Info(running ? "服务已启动" : "服务已停止");
    }

    private static int Status(ToolContext context, DeploymentState state, IServiceManager manager, ServiceSetup setup)
    {
        context.Log.Say($"运行方式：{(state.RunMode == RunMode.Service ? "服务" : "直接运行")}");
        bool allPresent = true;
        foreach (ServiceDefinition service in setup.Services)
        {
            ServiceStatus status = manager.Query(service.Name);
            allPresent &= status.Exists && status.Executable is not null && SamePath(status.Executable, service.Executable);
            string where = status.Executable is null || SamePath(status.Executable, service.Executable)
                ? string.Empty
                : $"，指向其他位置 {status.Executable}";
            context.Log.Say($"  {service.Name}：{(!status.Exists ? "不存在" : status.Running ? "运行中" : "已停止")}{where}");
        }

        // 管理员按输出的命令手动创建服务后，在这里补记。
        if (state.RunMode == RunMode.Service && state.ServicesInstalled != allPresent)
        {
            state.ServicesInstalled = allPresent;
            DeploymentStateStore.Write(context.Layout, state);
        }

        return 0;
    }

    private static async Task ReportReadyAsync(ToolContext context)
    {
        LocalConfig config = LocalConfig.Load(context.Layout.LocalConfigPath);
        using ReadinessProbe probe = new(HostEndpoint.Resolve(config, ConfigKeys.WEB, ConfigKeys.DEFAULT_PANEL_PORT, context.Layout.DataDirectory));
        if (await probe.WaitReadyAsync(null, CancellationToken.None)) context.Log.Info("宿主已就绪");
        else context.Log.Warning($"宿主未在 {ReadinessProbe.ReadyTimeout.TotalSeconds} 秒内就绪，请查看 host/Logs 下的日志");
    }

    private static string? DatabaseName(LocalConfig config) =>
        config.GetString(ConfigKeys.DATABASE_DSN) is { } dsn ? DsnFormat.Parse(dsn).Database : null;

    private static bool SamePath(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
