using System.Diagnostics;
using System.Runtime.InteropServices;
using Shinano.Tool.Cli;
using Shinano.Tool.Configuration;
using Shinano.Tool.Deployment;

namespace Shinano.Tool.Run;

/// <summary>
/// <c>run</c>：直接运行时的守护者。在前台启动监控（输出不进控制台，只写它自己的日志）与宿主（继承控制台），
/// 宿主以约定退出码退出时重新拉起，按 Ctrl+C 后等两者正常退出。重启后的就绪判定与再启动规则见 <see cref="SupervisorPolicy" />。
/// </summary>
public static class Supervisor
{
    /// <summary>该命令接受的选项。</summary>
    public static readonly IReadOnlyCollection<string> Options = [];

    private static readonly TimeSpan HostStopTimeout    = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MonitorStopTimeout = TimeSpan.FromSeconds(30);

    /// <summary>运行到管理员按 Ctrl+C 或宿主自身退出。</summary>
    /// <returns>守护者的退出码：正常停止为 0，宿主自身退出时为其退出码。</returns>
    public static async Task<int> RunAsync(ToolContext context)
    {
        DeploymentLayout layout = context.Layout;
        ToolLog log = context.Log;
        if (!File.Exists(layout.LocalConfigPath)) throw new ToolException("尚未初始化，请先运行 init");
        if (DeploymentStateStore.Read(layout) is { RunMode: RunMode.Service, ServicesInstalled: true })
            throw new ToolException("该部署以服务方式运行，请使用 service start；改为直接运行请重新运行 init 并选择直接运行");
        if (!File.Exists(layout.HostExecutable)) throw new ToolException($"找不到宿主程序 {layout.HostExecutable}");

        using CancellationTokenSource stop = new();
        LocalConfig config = LocalConfig.Load(layout.LocalConfigPath);
        using ReadinessProbe probe = new(HostEndpoint.Resolve(config, ConfigKeys.WEB, ConfigKeys.DEFAULT_PANEL_PORT, layout.DataDirectory));
        using Process? monitor = StartMonitor(layout, log);
        Process? current = null;

        // 控制台的 Ctrl+C、Ctrl+Break 同时送达同一控制台（进程组）中的宿主与监控，由它们自行正常退出，守护者只停止拉起并等待。
        // 单独发给守护者的 SIGTERM 不会到达子进程，在 Linux 上转发给它们。
        void OnSignal(PosixSignalContext signal)
        {
            signal.Cancel = true;
            if (signal.Signal == PosixSignal.SIGTERM && !OperatingSystem.IsWindows())
                foreach (Process? child in (Process?[]) [current, monitor])
                    if (child is { HasExited: false }) UnixSignal.Terminate(child.Id);
            if (stop.IsCancellationRequested) return;
            log.Say("[Shinano.Tool] 正在停止，等待宿主与监控退出……");
            stop.Cancel();
        }

        using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
        using PosixSignalRegistration quit = PosixSignalRegistration.Create(PosixSignal.SIGQUIT, OnSignal);
        using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);

        SupervisorDecision watch = SupervisorDecision.RestartAndReport;
        int restartsWithoutReady = 0;
        while (true)
        {
            using Process host = StartHost(layout, log);
            current = host;
            using CancellationTokenSource watching = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            Task<ReadyOutcome> readiness = WatchAsync(host, probe, watch, log, watching.Token);

            await WaitForExitAsync(host, stop.Token, log);
            watching.Cancel();
            ReadyOutcome outcome = await readiness;
            if (outcome == ReadyOutcome.Ready) restartsWithoutReady = 0;

            SupervisorDecision decision = SupervisorPolicy.AfterExit(
                host.ExitCode,
                stop.IsCancellationRequested,
                outcome == ReadyOutcome.Killed,
                File.Exists(Path.Combine(layout.DataDirectory, HostContract.RESTART_PLAN_PATH)),
                restartsWithoutReady);
            switch (decision)
            {
                case SupervisorDecision.Stop:
                    await StopMonitorAsync(monitor, signalled: true, log);
                    log.Info("宿主与监控已停止");
                    return 0;
                case SupervisorDecision.Exit:
                    log.Error($"宿主退出，退出码 {host.ExitCode}；请查看宿主日志");
                    await StopMonitorAsync(monitor, signalled: false, log);
                    return host.ExitCode;
                case SupervisorDecision.GiveUp:
                    log.Error($"宿主连续 {SupervisorPolicy.MAX_RESTARTS_WITHOUT_READY} 次重启都未能就绪，停止拉起；请查看宿主日志");
                    await StopMonitorAsync(monitor, signalled: false, log);
                    return 1;
                case SupervisorDecision.RestartAndReport:
                case SupervisorDecision.RestartAndRetryIfNotReady:
                    if (outcome != ReadyOutcome.Ready) restartsWithoutReady++;
                    log.Info(outcome == ReadyOutcome.Killed ? "再启动一次宿主，由宿主还原上一套插件包" : "宿主请求重启，重新拉起");
                    watch = decision;
                    break;
            }
        }
    }

    private static Process StartHost(DeploymentLayout layout, ToolLog log)
    {
        ProcessStartInfo info = new(layout.HostExecutable)
        {
            WorkingDirectory = layout.HostDirectory,
            UseShellExecute  = false
        };
        info.Environment[DeploymentLayout.DATA_DIRECTORY_VARIABLE] = layout.DataDirectory;
        info.Environment[HostContract.SUPERVISED_VARIABLE]         = HostContract.SUPERVISED_VALUE;
        Process process = Process.Start(info) ?? throw new ToolException($"无法启动宿主 {layout.HostExecutable}");
        log.Info($"已启动宿主，PID {process.Id}");
        return process;
    }

    private static Process? StartMonitor(DeploymentLayout layout, ToolLog log)
    {
        if (!File.Exists(layout.MonitorExecutable))
        {
            log.Warning($"找不到监控程序 {layout.MonitorExecutable}，只启动宿主");
            return null;
        }

        // 监控的输出不进控制台，避免与宿主日志混在一起；它自己的日志仍写入 monitor/Logs。
        ProcessStartInfo info = new(layout.MonitorExecutable)
        {
            WorkingDirectory       = layout.MonitorDirectory,
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        info.Environment[DeploymentLayout.DATA_DIRECTORY_VARIABLE] = layout.DataDirectory;
        Process process = Process.Start(info) ?? throw new ToolException($"无法启动监控 {layout.MonitorExecutable}");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived  += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            // 正常停止时监控同样会退出；这里只在宿主仍在运行期间报告。
            if (process.ExitCode != 0) log.Warning($"监控退出，退出码 {process.ExitCode}；宿主继续运行，请查看 monitor/Logs");
        };
        log.Info($"已启动监控，PID {process.Id}");
        return process;
    }

    private static async Task<ReadyOutcome> WatchAsync(
        Process            host,
        ReadinessProbe     probe,
        SupervisorDecision mode,
        ToolLog            log,
        CancellationToken  cancellationToken)
    {
        if (await probe.WaitReadyAsync(host, cancellationToken)) return ReadyOutcome.Ready;
        if (cancellationToken.IsCancellationRequested || host.HasExited) return ReadyOutcome.NotReady;

        if (mode != SupervisorDecision.RestartAndRetryIfNotReady)
        {
            log.Warning($"宿主未在 {ReadinessProbe.ReadyTimeout.TotalSeconds} 秒内就绪，保留该进程；请查看宿主日志");
            return ReadyOutcome.NotReady;
        }

        log.Error($"带插件包变更的新进程未在 {ReadinessProbe.ReadyTimeout.TotalSeconds} 秒内就绪，结束该进程");
        Kill(host, log);
        return ReadyOutcome.Killed;
    }

    private static async Task WaitForExitAsync(Process host, CancellationToken stop, ToolLog log)
    {
        try
        {
            await host.WaitForExitAsync(stop);
            return;
        }
        catch (OperationCanceledException)
        {
            // 管理员按了 Ctrl+C，宿主正在正常退出。
        }

        using CancellationTokenSource timeout = new(HostStopTimeout);
        try
        {
            await host.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            log.Warning($"宿主在 {HostStopTimeout.TotalSeconds} 秒内未退出，强制结束");
            Kill(host, log);
        }
    }

    // signalled 为 true 时 Ctrl+C 已送达监控，等它正常退出；宿主自身退出时监控没有收到信号，直接结束。
    private static async Task StopMonitorAsync(Process? monitor, bool signalled, ToolLog log)
    {
        if (monitor is null || monitor.HasExited) return;
        if (!signalled)
        {
            Kill(monitor, log);
            return;
        }

        using CancellationTokenSource timeout = new(MonitorStopTimeout);
        try
        {
            await monitor.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(monitor, log);
        }
    }

    private static void Kill(Process process, ToolLog log)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit();
            log.Warning($"已结束进程 {process.Id}");
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Error($"结束进程 {process.Id} 失败：{exception.Message}");
        }
    }
}
