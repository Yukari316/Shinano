using Microsoft.Extensions.Logging;

namespace Shinano.Infrastructure.Logging;

/// <summary>
/// 日志配置，对应配置节 <c>Shinano:Logging</c>。修改后需重启进程生效。
/// </summary>
public sealed class LoggingOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Logging";

    /// <summary>最低输出等级，默认 Information。</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// <c>Logs</c> 目录总大小上限，单位 MiB（1024×1024 字节），默认 2048。
    /// 超出时从修改时间最早的文件开始删除，当前正在写入的文件除外。
    /// </summary>
    public int MaxTotalSizeMiB { get; set; } = 2048;
}
