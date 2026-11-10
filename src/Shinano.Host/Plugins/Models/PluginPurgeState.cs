namespace Shinano.Host.Plugins.Models;

/// <summary>一次插件数据清除的进度。</summary>
public enum PluginPurgeState
{
    /// <summary>等待数据库与 RustFS 可用后执行。</summary>
    Waiting,

    /// <summary>正在执行。</summary>
    Running,

    /// <summary>全部步骤完成。</summary>
    Completed,

    /// <summary>有步骤失败，下次启动重试失败的步骤。</summary>
    PartiallyFailed,

    /// <summary>执行前发现同 ID 的插件已重新安装，放弃剩余步骤，避免删除新插件的数据。</summary>
    Cancelled
}
