namespace Shinano.Host.Restart.Models;

/// <summary>面板发起重启时由谁拉起新进程。</summary>
public enum RestartMode
{
    /// <summary>直接运行：主进程启动重启辅助程序后退出，由辅助程序启动新进程并等待就绪。</summary>
    Helper,

    /// <summary>由 Windows 服务或 systemd 托管：主进程以约定退出码退出，由服务管理器重启。</summary>
    ServiceManager
}
