using Shinano.Monitor.Web.Options;

namespace Shinano.Monitor.Configuration;

/// <summary>
/// 从主部署的本地配置文件读取的监控设置与探测目标。监控只读该文件，不写入；缺少的项使用与主进程相同的默认值。
/// 修改后重启监控进程生效。
/// </summary>
public sealed class MonitorSettings
{
    /// <summary>指定部署数据目录的环境变量，与主进程相同；设置时其中的 <c>shinano.local.json</c> 即部署配置。</summary>
    public const string DATA_DIRECTORY_VARIABLE = "SHINANO_DATA_DIR";

    /// <summary>部署配置文件名。</summary>
    public const string CONFIG_FILE_NAME = "shinano.local.json";

    /// <summary>监控自身的默认端口。</summary>
    public const int DEFAULT_PORT = 19101;

    /// <summary>主进程面板的默认端口。</summary>
    public const int DEFAULT_HOST_PORT = 19100;

    private const int    DEFAULT_INTERVAL_SECONDS = 30;
    private const int    MIN_INTERVAL_SECONDS     = 5;
    private const int    DEFAULT_MAX_LOG_MIB      = 256;
    private const int    DEFAULT_LLBOT_PORT       = 3000;
    private const string DEFAULT_LLBOT_PREFIX     = "milky";
    private const string DEFAULT_TIME_ZONE        = "Asia/Shanghai";

#if DEBUG
    private const string DEFAULT_SERVICE_NAME = "ShinanoMonitorDebug";
#else
    private const string DEFAULT_SERVICE_NAME = "ShinanoMonitor";
#endif

    /// <summary>部署配置文件的完整路径；未找到时为 null。</summary>
    public string? ConfigPath { get; private init; }

    /// <summary>部署数据目录，用于解析证书等相对路径。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>监控自身的监听配置。</summary>
    public required ListenOptions Listen { get; init; }

    /// <summary>探测间隔。</summary>
    public required TimeSpan Interval { get; init; }

    /// <summary>日志目录总大小上限（MiB）。</summary>
    public required int MaxLogSizeMiB { get; init; }

    /// <summary>Windows 服务与 systemd unit 名称。</summary>
    public required string ServiceName { get; init; }

    /// <summary>部署时区，决定探测记录的分日与页面时间。</summary>
    public required TimeZoneInfo TimeZone { get; init; }

    /// <summary>主进程面板的监听配置。</summary>
    public required ListenOptions HostWeb { get; init; }

    /// <summary>PostgreSQL DSN；未配置时为 null。</summary>
    public string? DatabaseDsn { get; init; }

    /// <summary>RustFS 内部地址；未配置时为 null。</summary>
    public string? ObjectStorageEndpoint { get; init; }

    /// <summary>LLBot 地址。</summary>
    public required string LlBotHost { get; init; }

    /// <summary>LLBot 端口。</summary>
    public required int LlBotPort { get; init; }

    /// <summary>LLBot 路径前缀。</summary>
    public required string LlBotPrefix { get; init; }

    /// <summary>LLBot AccessToken；未配置时为 null。</summary>
    public string? LlBotAccessToken { get; init; }

    /// <summary>
    /// 定位并读取部署配置：优先使用环境变量 <see cref="DATA_DIRECTORY_VARIABLE" />，其次使用监控
    /// <c>appsettings.json</c> 中的 <c>Monitor:DeploymentConfigPath</c>（相对路径基于程序目录）。
    /// </summary>
    /// <param name="configuredPath"><c>Monitor:DeploymentConfigPath</c> 的值。</param>
    /// <param name="baseDirectory">监控程序目录。</param>
    public static MonitorSettings Load(string? configuredPath, string baseDirectory)
    {
        string? dataDirectory = Environment.GetEnvironmentVariable(DATA_DIRECTORY_VARIABLE);
        string? path = !string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Path.GetFullPath(dataDirectory, baseDirectory), CONFIG_FILE_NAME)
            : string.IsNullOrWhiteSpace(configuredPath) ? null : Path.GetFullPath(configuredPath, baseDirectory);
        if (path is not null && !File.Exists(path)) path = null;

        IConfiguration configuration = path is null
            ? new ConfigurationBuilder().Build()
            : new ConfigurationBuilder().AddJsonFile(path, optional: false, reloadOnChange: false).Build();
        IConfigurationSection root    = configuration.GetSection("Shinano");
        IConfigurationSection monitor = root.GetSection("Monitor");
        IConfigurationSection llBot   = root.GetSection("LlBot");

        int interval = monitor.GetValue("IntervalSeconds", DEFAULT_INTERVAL_SECONDS);
        int maxLog   = monitor.GetValue("MaxLogSizeMiB", DEFAULT_MAX_LOG_MIB);
        string zone  = root.GetValue("Deployment:TimeZone", DEFAULT_TIME_ZONE) ?? DEFAULT_TIME_ZONE;
        string? serviceName = monitor.GetValue<string?>("ServiceName");
        return new MonitorSettings
        {
            ConfigPath            = path,
            DataDirectory         = path is null ? baseDirectory : Path.GetDirectoryName(path) ?? baseDirectory,
            Listen                = ReadListen(monitor, DEFAULT_PORT),
            Interval              = TimeSpan.FromSeconds(Math.Max(MIN_INTERVAL_SECONDS, interval)),
            MaxLogSizeMiB         = maxLog > 0 ? maxLog : DEFAULT_MAX_LOG_MIB,
            ServiceName           = string.IsNullOrWhiteSpace(serviceName) ? DEFAULT_SERVICE_NAME : serviceName.Trim(),
            TimeZone              = TimeZoneInfo.TryFindSystemTimeZoneById(zone, out TimeZoneInfo? timeZone) ? timeZone : TimeZoneInfo.Local,
            HostWeb               = ReadListen(root.GetSection("Web"), DEFAULT_HOST_PORT),
            DatabaseDsn           = Value(root, "Database:Dsn"),
            ObjectStorageEndpoint = Value(root, "RustFs:Endpoint"),
            LlBotHost             = Value(llBot, "Host") ?? "127.0.0.1",
            LlBotPort             = llBot.GetValue("Port", DEFAULT_LLBOT_PORT),
            LlBotPrefix           = llBot.GetValue<string?>("Prefix") ?? DEFAULT_LLBOT_PREFIX,
            LlBotAccessToken      = Value(llBot, "AccessToken")
        };
    }

    private static ListenOptions ReadListen(IConfigurationSection section, int defaultPort) =>
        new()
        {
            ListenAddress       = Value(section, "ListenAddress") ?? "127.0.0.1",
            Port                = section.GetValue("Port", defaultPort),
            CertificatePath     = Value(section, "CertificatePath"),
            CertificatePassword = Value(section, "CertificatePassword")
        };

    private static string? Value(IConfiguration section, string key) =>
        section[key] is { Length: > 0 } value ? value : null;
}
