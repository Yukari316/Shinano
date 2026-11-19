namespace Shinano.Monitor.Probing;

/// <summary>被监视的服务，按状态页显示顺序排列。</summary>
public enum MonitoredService
{
    /// <summary>Shinano 主进程，经其 <c>/healthz</c> 汇总数据库、RustFS、LLBot 与插件状态。</summary>
    Host,

    /// <summary>PostgreSQL。</summary>
    Database,

    /// <summary>RustFS。</summary>
    ObjectStorage,

    /// <summary>LLBot（Milky）。</summary>
    Bot
}
