namespace Shinano.Tool.Services.Models;

/// <summary>一条修改系统状态的命令。有权限时由工具执行，否则原样输出给管理员执行。</summary>
public sealed class SystemCommand
{
    /// <summary>可执行文件。</summary>
    public required string FileName { get; init; }

    /// <summary>参数，每项一个参数，不需要预先加引号。</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>这条命令做什么，写入操作记录。</summary>
    public required string Description { get; init; }

    /// <summary>除 0 以外同样视为成功的退出码，如服务已在运行时再次启动。</summary>
    public IReadOnlyList<int> AcceptedExitCodes { get; init; } = [];
}
