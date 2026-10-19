namespace Shinano.Infrastructure.Logging;

/// <summary>
/// 本次进程的日志文件位置。文件名为 <c>&lt;日期&gt;-&lt;启动时间&gt;.log</c>，
/// 单个文件超过 <see cref="FILE_SIZE_LIMIT_BYTES" /> 时由 Serilog 续写为 <c>&lt;名称&gt;_001.log</c> 等。
/// </summary>
public sealed class LogFileLayout
{
    /// <summary>日志目录名，位于程序启动目录下。</summary>
    public const string DIRECTORY_NAME = "Logs";

    /// <summary>日志文件扩展名。</summary>
    public const string FILE_EXTENSION = ".log";

    /// <summary>
    /// 单个日志文件的大小上限。长时间运行的进程分段写入，使目录清理可以删除本次运行中已写完的旧段。
    /// </summary>
    public const long FILE_SIZE_LIMIT_BYTES = 64L * 1024 * 1024;

    private const string FILE_NAME_FORMAT = "yyyy-MM-dd-HHmmss";

    private LogFileLayout(string directory, string runPrefix)
    {
        Directory = directory;
        RunPrefix = runPrefix;
    }

    /// <summary>日志目录的完整路径。</summary>
    public string Directory { get; }

    /// <summary>本次运行的文件名前缀（不含扩展名与分段后缀）。</summary>
    public string RunPrefix { get; }

    /// <summary>本次运行首个日志文件的完整路径。</summary>
    public string FirstFilePath => Path.Combine(Directory, RunPrefix + FILE_EXTENSION);

    /// <summary>
    /// 在 <paramref name="baseDirectory" /> 下确定本次运行的日志布局，并在目录不存在时创建。
    /// 同一秒内多次启动时追加序号，避免覆盖。
    /// </summary>
    /// <param name="baseDirectory">程序启动目录。</param>
    /// <param name="startTime">进程启动的本地时间。</param>
    public static LogFileLayout Create(string baseDirectory, DateTime startTime)
    {
        string directory = Path.Combine(baseDirectory, DIRECTORY_NAME);
        System.IO.Directory.CreateDirectory(directory);

        string basePrefix = startTime.ToString(FILE_NAME_FORMAT);
        string runPrefix  = basePrefix;
        int    sequence   = 1;
        while (System.IO.Directory.EnumerateFiles(directory, runPrefix + "*" + FILE_EXTENSION).Any())
        {
            sequence++;
            runPrefix = $"{basePrefix}-{sequence}";
        }

        return new LogFileLayout(directory, runPrefix);
    }
}
