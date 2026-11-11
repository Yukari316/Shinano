namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>NB2 进程的状态。</summary>
public enum Nb2ProcessState
{
    /// <summary>未运行。</summary>
    Stopped,

    /// <summary>进程已启动，尚未连上或尚未就绪（含断线重连期间）。</summary>
    Starting,

    /// <summary>已连上并推送禁用集合，正在交付事件。</summary>
    Running,

    /// <summary>正在停止。</summary>
    Stopping,

    /// <summary>1 分钟内意外退出次数超过上限，已停止自动重启，需要手动启动。</summary>
    Failed,

    /// <summary>没有 NB2 运行环境（未运行 nb2-setup）。</summary>
    Missing
}
