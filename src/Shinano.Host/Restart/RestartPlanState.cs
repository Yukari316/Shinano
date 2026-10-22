namespace Shinano.Host.Restart;

/// <summary>重启计划的进度；验证通过后计划即删除，因此除 <see cref="Pending" /> 外的任何状态在启动时都会还原备份。</summary>
public enum RestartPlanState
{
    /// <summary>已由重启操作写入，等待新进程启动时应用。</summary>
    Pending,

    /// <summary>正在应用；进程在此期间退出时下一次启动还原。</summary>
    Applying,

    /// <summary>已应用，等待新进程验证插件包加载。</summary>
    Applied,

    /// <summary>变更的插件包加载失败，下一次启动还原。</summary>
    LoadFailed
}
