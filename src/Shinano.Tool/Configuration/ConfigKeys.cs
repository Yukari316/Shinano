namespace Shinano.Tool.Configuration;

/// <summary>
/// 工具读写的本地配置项路径，格式与 ASP.NET Core 配置路径相同。工具不引用主工程，这些路径与宿主、监控的选项类
/// 必须一致，由契约测试核对。
/// </summary>
public static class ConfigKeys
{
    /// <summary>部署时区。</summary>
    public const string TIME_ZONE = "Shinano:Deployment:TimeZone";

    /// <summary>bucket 前缀。</summary>
    public const string BUCKET_PREFIX = "Shinano:Deployment:BucketPrefix";

    /// <summary>Super User ID 列表。</summary>
    public const string SUPER_USERS = "Shinano:Deployment:SuperUsers";

    /// <summary>PostgreSQL 连接串（含凭据）。</summary>
    public const string DATABASE_DSN = "Shinano:Database:Dsn";

    /// <summary>RustFS 内部地址。</summary>
    public const string RUSTFS_ENDPOINT = "Shinano:RustFs:Endpoint";

    /// <summary>RustFS 的 IM 可达地址。</summary>
    public const string RUSTFS_IM_ENDPOINT = "Shinano:RustFs:ImEndpoint";

    /// <summary>RustFS AccessKey。</summary>
    public const string RUSTFS_ACCESS_KEY = "Shinano:RustFs:AccessKey";

    /// <summary>RustFS SecretKey。</summary>
    public const string RUSTFS_SECRET_KEY = "Shinano:RustFs:SecretKey";

    /// <summary>LLBot 主机。</summary>
    public const string LLBOT_HOST = "Shinano:LlBot:Host";

    /// <summary>LLBot 端口。</summary>
    public const string LLBOT_PORT = "Shinano:LlBot:Port";

    /// <summary>Milky 路径前缀。</summary>
    public const string LLBOT_PREFIX = "Shinano:LlBot:Prefix";

    /// <summary>LLBot AccessToken。</summary>
    public const string LLBOT_ACCESS_TOKEN = "Shinano:LlBot:AccessToken";

    /// <summary>插件目录，相对路径基于部署数据目录。</summary>
    public const string PLUGIN_DIRECTORY = "Shinano:Plugins:Directory";

    /// <summary>面板监听配置节。</summary>
    public const string WEB = "Shinano:Web";

    /// <summary>面板登录 Token。</summary>
    public const string PANEL_TOKEN = "Shinano:Web:Token";

    /// <summary>宿主服务名。</summary>
    public const string HOST_SERVICE_NAME = "Shinano:Restart:ServiceName";

    /// <summary>状态页（监控）配置节，监听配置与面板同名。</summary>
    public const string MONITOR = "Shinano:Monitor";

    /// <summary>监控服务名。</summary>
    public const string MONITOR_SERVICE_NAME = "Shinano:Monitor:ServiceName";

    /// <summary>监听配置节下的监听地址。</summary>
    public const string LISTEN_ADDRESS = "ListenAddress";

    /// <summary>监听配置节下的端口。</summary>
    public const string PORT = "Port";

    /// <summary>监听配置节下的 PFX 证书路径，相对路径基于部署数据目录。</summary>
    public const string CERTIFICATE_PATH = "CertificatePath";

    /// <summary>监听配置节下的 PFX 证书密码。</summary>
    public const string CERTIFICATE_PASSWORD = "CertificatePassword";

    /// <summary>未配置时宿主使用的 bucket 前缀。</summary>
    public const string DEFAULT_BUCKET_PREFIX = "shinano";

    /// <summary>默认插件目录。</summary>
    public const string DEFAULT_PLUGIN_DIRECTORY = "plugins";

    /// <summary>面板默认端口。</summary>
    public const int DEFAULT_PANEL_PORT = 19100;

    /// <summary>状态页默认端口。</summary>
    public const int DEFAULT_STATUS_PORT = 19101;

    /// <summary>拼接配置节与配置项。</summary>
    public static string In(string section, string key) => $"{section}:{key}";
}
