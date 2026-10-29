namespace Shinano.Tool.Services.Models;

/// <summary>要创建的一个系统服务。</summary>
public sealed class ServiceDefinition
{
    /// <summary>服务名。</summary>
    public required string Name { get; init; }

    /// <summary>说明。</summary>
    public required string Description { get; init; }

    /// <summary>可执行文件的完整路径。</summary>
    public required string Executable { get; init; }

    /// <summary>工作目录。</summary>
    public required string WorkingDirectory { get; init; }
}
