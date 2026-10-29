namespace Shinano.Tool.Services.Models;

/// <summary>向服务管理器查询到的服务状态。</summary>
public sealed class ServiceStatus
{
    /// <summary>服务是否存在。</summary>
    public required bool Exists { get; init; }

    /// <summary>服务是否在运行。</summary>
    public required bool Running { get; init; }

    /// <summary>服务的可执行文件；不存在或无法读取时为 null。</summary>
    public string? Executable { get; init; }
}
