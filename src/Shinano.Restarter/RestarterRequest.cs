namespace Shinano.Restarter;

/// <summary>宿主交给重启辅助程序的请求，以 JSON 文件传递，路径由命令行参数 <c>--request</c> 给出。</summary>
public sealed class RestarterRequest
{
    /// <summary>命令行中指定请求文件的参数名。</summary>
    public const string REQUEST_ARGUMENT = "--request";

    /// <summary>旧宿主进程 ID；辅助程序等它退出后再启动新进程。</summary>
    public required int HostProcessId { get; init; }

    /// <summary>启动宿主的可执行文件。</summary>
    public required string Command { get; init; }

    /// <summary>启动参数。</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>工作目录。</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>新进程的就绪接口地址，按新配置解析的监听端点得出。</summary>
    public required string ReadyUrl { get; init; }

    /// <summary>启用 HTTPS 时面板证书的 SHA-256 指纹；辅助程序只信任该证书。HTTP 时为 null。</summary>
    public string? CertificateThumbprint { get; init; }

    /// <summary>
    /// 是否带插件包变更。为真时新进程未能就绪即结束它并再启动一次：宿主启动时发现未验证的变更会自行还原上一套插件包。
    /// </summary>
    public required bool RollbackOnFailure { get; init; }

    /// <summary>日志目录，辅助程序在其中写独立的日志文件。</summary>
    public required string LogDirectory { get; init; }
}
