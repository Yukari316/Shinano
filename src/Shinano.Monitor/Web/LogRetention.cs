namespace Shinano.Monitor.Web;

/// <summary>
/// 按总大小清理监控的日志目录：超过上限时从修改时间最早的文件开始删除，本次运行的日志文件不删除。
/// 规则与主进程相同，为保持监控独立在本工程内实现。
/// </summary>
public sealed class LogRetention
{
    private const long   BYTES_PER_MIB  = 1024L * 1024;
    private const string FILE_EXTENSION = ".log";

    private readonly string                _directory;
    private readonly string                _runPrefix;
    private readonly long                  _maxBytes;
    private readonly ILogger<LogRetention> _logger;

    /// <summary>创建清理器。</summary>
    public LogRetention(string directory, string runPrefix, int maxSizeMiB, ILogger<LogRetention> logger)
    {
        _directory = directory;
        _runPrefix = runPrefix;
        _maxBytes  = maxSizeMiB * BYTES_PER_MIB;
        _logger    = logger;
    }

    /// <summary>执行一次清理；删除失败的文件记录警告后跳过。</summary>
    public void Clean()
    {
        List<FileInfo> files =
        [
            .. new DirectoryInfo(_directory).EnumerateFiles("*" + FILE_EXTENSION)
                                            .Where(file => !file.Name.StartsWith(_runPrefix, StringComparison.Ordinal))
                                            .OrderBy(file => file.LastWriteTimeUtc)
        ];
        long total = new DirectoryInfo(_directory).EnumerateFiles("*" + FILE_EXTENSION).Sum(file => file.Length);
        foreach (FileInfo file in files)
        {
            if (total <= _maxBytes) break;
            long length = file.Length;
            try
            {
                file.Delete();
                total -= length;
                _logger.LogInformation("日志目录超过 {MaxSizeMiB} MiB，已删除 {File}", _maxBytes / BYTES_PER_MIB, file.Name);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(exception, "无法删除日志文件 {File}", file.Name);
            }
        }
    }
}
