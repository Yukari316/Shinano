namespace Shinano.Tool.Services.Models;

/// <summary>一个部署的两个服务（宿主与监控）及其共用的设置。</summary>
public sealed class ServiceSetup
{
    /// <summary>部署根目录；Windows 上给虚拟服务账户授予修改权限，Linux 上改为专用用户所有。</summary>
    public required string Root { get; init; }

    /// <summary>部署数据目录，以环境变量交给服务。</summary>
    public required string DataDirectory { get; init; }

    /// <summary>宿主服务。</summary>
    public required ServiceDefinition Host { get; init; }

    /// <summary>监控服务。</summary>
    public required ServiceDefinition Monitor { get; init; }

    /// <summary>Linux 上运行服务的用户。</summary>
    public required string User { get; init; }

    /// <summary>Linux 上 <see cref="User" /> 是否为专用用户：不存在时创建，并把部署根目录改为该用户所有。</summary>
    public required bool DedicatedUser { get; init; }

    /// <summary>两个服务，按启动顺序排列。</summary>
    public IReadOnlyList<ServiceDefinition> Services => [Host, Monitor];
}
