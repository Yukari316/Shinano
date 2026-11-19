namespace Shinano.Tool.Deployment;

/// <summary>工具的部署状态，保存在部署数据目录的 <c>deployment.json</c>；宿主不读取。</summary>
public sealed class DeploymentState
{
    /// <summary>默认的宿主服务名。</summary>
    public const string DEFAULT_HOST_SERVICE_NAME = "Shinano";

    /// <summary>默认的监控服务名。</summary>
    public const string DEFAULT_MONITOR_SERVICE_NAME = "ShinanoMonitor";

    /// <summary>运行方式。</summary>
    public RunMode RunMode { get; set; } = RunMode.Direct;

    /// <summary>宿主服务名。</summary>
    public string HostServiceName { get; set; } = DEFAULT_HOST_SERVICE_NAME;

    /// <summary>监控服务名。</summary>
    public string MonitorServiceName { get; set; } = DEFAULT_MONITOR_SERVICE_NAME;

    /// <summary>服务是否已由工具创建；运行方式为服务但创建未完成（如权限不足只输出了命令）时为 false。</summary>
    public bool ServicesInstalled { get; set; }

    /// <summary>Linux 上运行服务的用户；为 null 时以执行安装的用户运行。</summary>
    public string? ServiceUser { get; set; }

    /// <summary><see cref="ServiceUser" /> 是否由工具创建；卸载时只询问删除工具创建的用户。</summary>
    public bool ServiceUserCreated { get; set; }

    /// <summary>装入过的原生插件 ID。升级时据此区分管理员删除的插件与新版本新增的插件，前者不再装回。</summary>
    public List<string> BundledPlugins { get; set; } = [];
}

/// <summary>部署的运行方式。</summary>
public enum RunMode
{
    /// <summary>直接运行：<c>start</c> 脚本在前台运行 <c>Shinano.Tool run</c>，由它守护宿主与监控。</summary>
    Direct,

    /// <summary>由 Windows 服务管理器或 systemd 托管。</summary>
    Service
}
