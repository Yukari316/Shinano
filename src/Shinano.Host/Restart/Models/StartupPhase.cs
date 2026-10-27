namespace Shinano.Host.Restart.Models;

/// <summary>进程的启动阶段，由就绪接口报告。</summary>
public enum StartupPhase
{
    /// <summary>启动流程尚未结束。</summary>
    Starting,

    /// <summary>启动流程结束，插件已加载。</summary>
    Ready,

    /// <summary>基础设施启动检查失败，Bot 不上线，面板继续提供故障与日志查看。重启时视为就绪。</summary>
    FaultMode
}
