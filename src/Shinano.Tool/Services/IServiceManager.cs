using Shinano.Tool.Services.Models;

namespace Shinano.Tool.Services;

/// <summary>
/// 系统服务管理器。修改操作以命令列表给出，由调用方在有权限时执行，或输出给管理员执行；查询不需要权限。
/// </summary>
public interface IServiceManager
{
    /// <summary>不需要权限的准备工作，如写出 unit 文件；在执行安装命令之前调用，可以重复调用。</summary>
    void Prepare(ServiceSetup setup);

    /// <summary>创建两个服务的命令，不含启动。</summary>
    IReadOnlyList<SystemCommand> InstallCommands(ServiceSetup setup);

    /// <summary>删除已存在的服务的命令；调用方先停止服务。</summary>
    /// <param name="setup">服务设置。</param>
    /// <param name="deleteUser">Linux 上是否同时删除专用用户。</param>
    /// <param name="owner">删除专用用户前，部署根目录改为哪个用户所有；为 null 时不改。</param>
    IReadOnlyList<SystemCommand> UninstallCommands(ServiceSetup setup, bool deleteUser, string? owner);

    /// <summary>启动两个服务的命令。</summary>
    IReadOnlyList<SystemCommand> StartCommands(ServiceSetup setup);

    /// <summary>停止两个服务的命令，按启动的相反顺序。</summary>
    IReadOnlyList<SystemCommand> StopCommands(ServiceSetup setup);

    /// <summary>查询一个服务。</summary>
    ServiceStatus Query(string name);

    /// <summary>等待服务进入运行或停止状态。</summary>
    /// <returns>是否在时限内达到。</returns>
    Task<bool> WaitAsync(string name, bool running, TimeSpan timeout);
}
