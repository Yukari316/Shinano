using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Shinano.Monitor.Web;

/// <summary>
/// 监控进程的日志：写入程序目录下的 <c>Logs</c>，文件名 <c>&lt;日期&gt;-&lt;启动时间&gt;.log</c>、输出格式与主进程相同，
/// 同一秒内多次启动时追加序号，不互相覆盖。
/// </summary>
public sealed class MonitorLogging : IDisposable
{
    private const string DIRECTORY_NAME     = "Logs";
    private const string FILE_NAME_FORMAT   = "yyyy-MM-dd-HHmmss";
    private const string FILE_EXTENSION     = ".log";
    private const string ASP_NET_CORE       = "Microsoft.AspNetCore";
    private const string OUTPUT_TEMPLATE    =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    private MonitorLogging(Logger logger, string directory, string runPrefix)
    {
        Logger    = logger;
        Directory = directory;
        RunPrefix = runPrefix;
    }

    /// <summary>Serilog 记录器，归本类所有。</summary>
    public Logger Logger { get; }

    /// <summary>日志目录的完整路径。</summary>
    public string Directory { get; }

    /// <summary>本次运行的文件名前缀（不含扩展名），清理时不删除这些文件。</summary>
    public string RunPrefix { get; }

    /// <summary>在 <paramref name="baseDirectory" /> 下创建日志目录与本次运行的记录器。</summary>
    public static MonitorLogging Create(string baseDirectory)
    {
        string directory = Path.Combine(baseDirectory, DIRECTORY_NAME);
        System.IO.Directory.CreateDirectory(directory);
        string basePrefix = DateTime.Now.ToString(FILE_NAME_FORMAT);
        string runPrefix  = basePrefix;
        for (int sequence = 2; System.IO.Directory.EnumerateFiles(directory, runPrefix + "*" + FILE_EXTENSION).Any(); sequence++)
            runPrefix = $"{basePrefix}-{sequence}";

        Logger logger = new LoggerConfiguration()
                        .MinimumLevel.Information()
                        // 请求级日志量大，只保留警告以上。
                        .MinimumLevel.Override(ASP_NET_CORE, LogEventLevel.Warning)
                        .WriteTo.Console(outputTemplate: OUTPUT_TEMPLATE)
                        .WriteTo.File(Path.Combine(directory, runPrefix + FILE_EXTENSION), outputTemplate: OUTPUT_TEMPLATE)
                        .CreateLogger();
        return new MonitorLogging(logger, directory, runPrefix);
    }

    /// <summary>刷新并关闭日志输出。</summary>
    public void Dispose() => Logger.Dispose();
}
