using System.Runtime.Versioning;
using System.ServiceProcess;
using Microsoft.Win32;
using Shinano.Tool.Deployment;
using Shinano.Tool.Services.Models;

namespace Shinano.Tool.Services;

/// <summary>
/// Windows 服务：虚拟服务账户 <c>NT SERVICE\&lt;服务名&gt;</c>、延迟自动启动、失败（含非崩溃的非零退出）后重启，
/// 部署数据目录经服务注册表的 <c>Environment</c> 交给进程，部署根目录授予该账户修改权限。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceManager : IServiceManager
{
    private const string SC                    = "sc.exe";
    private const string REG                   = "reg.exe";
    private const string ICACLS                = "icacls.exe";
    private const string SERVICES_KEY          = @"SYSTEM\CurrentControlSet\Services";
    private const string IMAGE_PATH_VALUE      = "ImagePath";
    private const string VIRTUAL_ACCOUNT       = @"NT SERVICE\";
    private const string FAILURE_RESET_SECONDS = "86400";
    private const string FAILURE_ACTIONS       = "restart/5000/restart/5000/restart/5000";

    // 服务已在运行时再次启动、未运行时停止，sc.exe 分别返回这两个错误码，都视为已达到目标状态。
    private const int ERROR_SERVICE_ALREADY_RUNNING = 1056;
    private const int ERROR_SERVICE_NOT_ACTIVE      = 1062;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public void Prepare(ServiceSetup setup)
    {
    }

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> InstallCommands(ServiceSetup setup) =>
    [
        .. setup.Services.SelectMany(service => (SystemCommand[])
        [
            Command(
                SC,
                $"创建服务 {service.Name}",
                "create",
                service.Name,
                "binPath=",
                $"\"{service.Executable}\"",
                "start=",
                "delayed-auto",
                "obj=",
                VIRTUAL_ACCOUNT + service.Name,
                "DisplayName=",
                service.Name),
            Command(SC, $"设置服务 {service.Name} 的说明", "description", service.Name, service.Description),
            Command(SC, $"设置服务 {service.Name} 失败后重启", "failure", service.Name, "reset=", FAILURE_RESET_SECONDS, "actions=", FAILURE_ACTIONS),
            // 宿主请求重启时以非零退出码正常退出，不是崩溃，需要开启该项才会执行恢复动作。
            Command(SC, $"设置服务 {service.Name} 非崩溃退出也执行恢复", "failureflag", service.Name, "1"),
            Command(
                REG,
                $"设置服务 {service.Name} 的部署数据目录",
                "add",
                $@"HKLM\{SERVICES_KEY}\{service.Name}",
                "/v",
                "Environment",
                "/t",
                "REG_MULTI_SZ",
                "/d",
                $"{DeploymentLayout.DATA_DIRECTORY_VARIABLE}={setup.DataDirectory}",
                "/f"),
            Command(ICACLS, $"授予服务 {service.Name} 部署目录的修改权限", setup.Root, "/grant", $"{VIRTUAL_ACCOUNT}{service.Name}:(OI)(CI)M")
        ])
    ];

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> UninstallCommands(ServiceSetup setup, bool deleteUser, string? owner) =>
    [
        .. setup.Services.Where(service => Query(service.Name).Exists).SelectMany(service => (SystemCommand[])
        [
            // 服务删除后虚拟账户名无法再解析，先移除授权。
            Command(ICACLS, $"移除服务 {service.Name} 对部署目录的权限", setup.Root, "/remove", VIRTUAL_ACCOUNT + service.Name),
            Command(SC, $"删除服务 {service.Name}", "delete", service.Name)
        ])
    ];

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> StartCommands(ServiceSetup setup) =>
    [
        .. setup.Services.Select(service => Command(SC, $"启动服务 {service.Name}", ERROR_SERVICE_ALREADY_RUNNING, "start", service.Name))
    ];

    /// <inheritdoc />
    public IReadOnlyList<SystemCommand> StopCommands(ServiceSetup setup) =>
    [
        .. setup.Services.Reverse().Select(service => Command(SC, $"停止服务 {service.Name}", ERROR_SERVICE_NOT_ACTIVE, "stop", service.Name))
    ];

    /// <inheritdoc />
    public ServiceStatus Query(string name)
    {
        using ServiceController? controller = ServiceController.GetServices()
                                                               .FirstOrDefault(service => service.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (controller is null)
            return new ServiceStatus
            {
                Exists  = false,
                Running = false
            };

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey($@"{SERVICES_KEY}\{name}");
        string? imagePath = key?.GetValue(IMAGE_PATH_VALUE) as string;
        return new ServiceStatus
        {
            Exists     = true,
            Running    = controller.Status == ServiceControllerStatus.Running,
            Executable = imagePath?.Trim().Trim('"')
        };
    }

    /// <inheritdoc />
    public async Task<bool> WaitAsync(string name, bool running, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            using ServiceController controller = new(name);
            try
            {
                controller.Refresh();
                if (controller.Status == (running ? ServiceControllerStatus.Running : ServiceControllerStatus.Stopped)) return true;
            }
            catch (InvalidOperationException)
            {
                // 服务不存在时视为已停止。
                if (!running) return true;
            }

            await Task.Delay(PollInterval);
        }

        return false;
    }

    private static SystemCommand Command(string fileName, string description, params string[] arguments) =>
        new()
        {
            FileName    = fileName,
            Arguments   = arguments,
            Description = description
        };

    private static SystemCommand Command(string fileName, string description, int acceptedExitCode, params string[] arguments) =>
        new()
        {
            FileName          = fileName,
            Arguments         = arguments,
            Description       = description,
            AcceptedExitCodes = [acceptedExitCode]
        };
}
