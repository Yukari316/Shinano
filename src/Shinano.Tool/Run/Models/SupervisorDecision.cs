namespace Shinano.Tool.Run.Models;

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
