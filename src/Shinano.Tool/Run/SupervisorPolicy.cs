namespace Shinano.Tool.Run;

/// <summary>
/// 守护者在宿主退出后的决定。规则：
/// <list type="bullet">
/// <item>管理员按 Ctrl+C 后不再拉起。</item>
/// <item>宿主以约定退出码退出（面板重启或插件包验证失败）时重新拉起；本次带插件包变更时，新进程未能及时就绪即结束它再启动一次。</item>
/// <item>因未能就绪而被结束的进程再启动一次，由宿主还原上一套插件包；这次即使仍未就绪也只记录，不再结束。</item>
/// <item>其他退出码视为宿主自身退出，守护者以同一退出码结束。</item>
/// </list>
/// </summary>
public static class SupervisorPolicy
{
    /// <summary>连续多少次重启都没能就绪后放弃，避免宿主每次启动都请求重启时无限循环。</summary>
    public const int MAX_RESTARTS_WITHOUT_READY = 3;

    /// <summary>首次启动宿主时的看护决定：存在未应用的重启计划时，未在时限内就绪同样触发结束并再拉起以还原。</summary>
    public static SupervisorDecision InitialWatch(bool planPending) =>
        planPending ? SupervisorDecision.RestartAndRetryIfNotReady : SupervisorDecision.RestartAndReport;

    /// <summary>宿主退出后的决定。</summary>
    /// <param name="exitCode">宿主退出码。</param>
    /// <param name="stopRequested">管理员是否已要求停止。</param>
    /// <param name="killedNotReady">该进程是否因未能及时就绪被守护者结束。</param>
    /// <param name="planPending">部署数据目录中是否有插件包变更计划。</param>
    /// <param name="restartsWithoutReady">此前连续重启且未就绪的次数。</param>
    public static SupervisorDecision AfterExit(int exitCode, bool stopRequested, bool killedNotReady, bool planPending, int restartsWithoutReady)
    {
        if (stopRequested) return SupervisorDecision.Stop;
        if (restartsWithoutReady >= MAX_RESTARTS_WITHOUT_READY) return SupervisorDecision.GiveUp;
        if (killedNotReady) return SupervisorDecision.RestartAndReport;
        if (exitCode == HostContract.RESTART_EXIT_CODE)
            return planPending ? SupervisorDecision.RestartAndRetryIfNotReady : SupervisorDecision.RestartAndReport;
        return SupervisorDecision.Exit;
    }
}

/// <summary>宿主退出后守护者的决定。</summary>
public enum SupervisorDecision
{
    /// <summary>管理员要求停止：不再拉起，等监控退出后结束。</summary>
    Stop,

    /// <summary>重新拉起；未能及时就绪时只记录。</summary>
    RestartAndReport,

    /// <summary>重新拉起；未能及时就绪时结束它并再启动一次，由宿主还原上一套插件包。</summary>
    RestartAndRetryIfNotReady,

    /// <summary>宿主自身退出：守护者以同一退出码结束。</summary>
    Exit,

    /// <summary>连续重启都没能就绪：停止拉起并报告。</summary>
    GiveUp
}
