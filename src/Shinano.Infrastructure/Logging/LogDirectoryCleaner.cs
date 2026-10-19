using Microsoft.Extensions.Logging;

namespace Shinano.Infrastructure.Logging;

/// <summary>
/// 按总大小清理日志目录：超过上限时从修改时间最早的文件开始删除，直到低于上限。
/// 本次运行最近写入的文件视为正在写入，不删除。
/// </summary>
public sealed class LogDirectoryCleaner
{
    private const long BYTES_PER_MIB = 1024L * 1024;

    private readonly LogFileLayout _layout;
    private readonly long          _maxTotalBytes;
    private readonly ILogger       _logger;

    /// <summary>创建清理器。</summary>
    /// <param name="layout">本次运行的日志布局。</param>
    /// <param name="maxTotalSizeMiB">目录总大小上限，单位 MiB，必须为正数。</param>
    /// <param name="logger">记录删除结果与失败原因。</param>
    public LogDirectoryCleaner(LogFileLayout layout, int maxTotalSizeMiB, ILogger logger)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTotalSizeMiB);
        _layout        = layout;
        _maxTotalBytes = maxTotalSizeMiB * BYTES_PER_MIB;
        _logger        = logger;
    }

    /// <summary>
    /// 执行一次清理。删除失败的文件记录警告后跳过，不抛出异常。
    /// </summary>
    /// <returns>本次删除的文件数。</returns>
    public int Clean()
    {
        List<FileInfo> files =
        [
            .. new DirectoryInfo(_layout.Directory)
               .EnumerateFiles("*" + LogFileLayout.FILE_EXTENSION)
               .OrderBy(file => file.LastWriteTimeUtc)
        ];
        long totalBytes = files.Sum(file => file.Length);
        if (totalBytes <= _maxTotalBytes) return 0;

        FileInfo? activeFile = files
                               .Where(file => file.Name.StartsWith(_layout.RunPrefix, StringComparison.Ordinal))
                               .MaxBy(file => file.LastWriteTimeUtc);
        int deleted = 0;
        foreach (FileInfo file in files)
        {
            if (totalBytes <= _maxTotalBytes) break;
            if (file.FullName == activeFile?.FullName) continue;

            long length = file.Length;
            try
            {
                file.Delete();
                totalBytes -= length;
                deleted++;
            }
            catch (IOException exception)
            {
                _logger.LogWarning(exception, "无法删除日志文件 {LogFile}", file.Name);
            }
            catch (UnauthorizedAccessException exception)
            {
                _logger.LogWarning(exception, "无权删除日志文件 {LogFile}", file.Name);
            }
        }

        if (deleted > 0)
            _logger.LogInformation(
                "日志目录超过 {MaxTotalSizeMiB} MiB，已删除 {DeletedCount} 个最早的日志文件",
                _maxTotalBytes / BYTES_PER_MIB,
                deleted);
        return deleted;
    }
}
