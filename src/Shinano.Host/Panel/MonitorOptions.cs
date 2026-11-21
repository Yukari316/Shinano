namespace Shinano.Host.Panel;

/// <summary>
/// 部署配置中 <c>Shinano:Monitor</c> 节在宿主一侧的视图，供宿主配置页显示默认值与构造状态页链接。
/// 监控是独立进程、宿主不能引用它，默认值与监控的 <c>MonitorSettings</c> 由契约测试保证一致。
/// </summary>
public sealed class MonitorOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Monitor";

    /// <summary>状态页默认端口。</summary>
    public const int DEFAULT_PORT = 19101;

    /// <summary>状态页默认探测间隔（秒）。</summary>
    public const int DEFAULT_INTERVAL_SECONDS = 30;

    /// <summary>监控日志目录默认上限（MiB）。</summary>
    public const int DEFAULT_MAX_LOG_SIZE_MIB = 256;

    /// <summary>状态页监听地址。</summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    /// <summary>状态页端口。</summary>
    public int Port { get; set; } = DEFAULT_PORT;

    /// <summary>探测间隔（秒）。</summary>
    public int IntervalSeconds { get; set; } = DEFAULT_INTERVAL_SECONDS;

    /// <summary>监控日志目录上限（MiB）。</summary>
    public int MaxLogSizeMiB { get; set; } = DEFAULT_MAX_LOG_SIZE_MIB;
}
