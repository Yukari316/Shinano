using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;

namespace Shinano.Tool.Services;

/// <summary>按平台选择服务管理器，并按部署状态组装服务设置。</summary>
public static class ServiceManagers
{
    private const string HOST_DESCRIPTION    = "Shinano 机器人宿主";
    private const string MONITOR_DESCRIPTION = "Shinano 服务状态页";
    private const string SUDO_USER_VARIABLE  = "SUDO_USER";

    /// <summary>当前平台的服务管理器。</summary>
    /// <exception cref="ToolException">当前平台不支持服务模式。</exception>
    public static IServiceManager ForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new WindowsServiceManager();
        if (OperatingSystem.IsLinux()) return new SystemdServiceManager();
        throw new ToolException("服务模式只支持 Windows 与 Linux（systemd）");
    }

    /// <summary>按部署状态组装两个服务的设置。</summary>
    public static ServiceSetup Setup(DeploymentLayout layout, DeploymentState state) =>
        new()
        {
            Root          = layout.Root,
            DataDirectory = layout.DataDirectory,
            Host = new ServiceDefinition
            {
                Name             = state.HostServiceName,
                Description      = HOST_DESCRIPTION,
                Executable       = layout.HostExecutable,
                WorkingDirectory = layout.HostDirectory
            },
            Monitor = new ServiceDefinition
            {
                Name             = state.MonitorServiceName,
                Description      = MONITOR_DESCRIPTION,
                Executable       = layout.MonitorExecutable,
                WorkingDirectory = layout.MonitorDirectory
            },
            User          = state.ServiceUser ?? InvokingUser(),
            DedicatedUser = state.ServiceUser is not null
        };

    /// <summary>执行工具的用户；经 <c>sudo</c> 运行时为调用 <c>sudo</c> 的用户。</summary>
    public static string InvokingUser() =>
        Environment.GetEnvironmentVariable(SUDO_USER_VARIABLE) is { Length: > 0 } sudoUser ? sudoUser : Environment.UserName;
}
