using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Infrastructure.Logging;

namespace Shinano.Tests.Infrastructure;

public sealed class LogDirectoryCleanerTests : IDisposable
{
    private const int MIB = 1024 * 1024;

    private readonly string _baseDirectory = Directory.CreateTempSubdirectory("shinano-log-").FullName;

    public void Dispose() => Directory.Delete(_baseDirectory, recursive: true);

    [Fact]
    public void OverLimit_DeletesOldestUntilBelowLimit_AndKeepsActiveFile()
    {
        LogFileLayout layout = LogFileLayout.Create(_baseDirectory, new DateTime(2026, 10, 18, 12, 0, 0));
        DateTime now = DateTime.UtcNow;
        string oldest = WriteFile(layout, "2026-10-01-080000.log", MIB, now.AddDays(-17));
        string older  = WriteFile(layout, "2026-10-10-080000.log", MIB, now.AddDays(-8));
        string recent = WriteFile(layout, "2026-10-17-080000.log", MIB, now.AddDays(-1));
        // 本次运行最早写入的文件也是全目录最早的，但它是当前文件，必须保留。
        string active = WriteFile(layout, layout.RunPrefix + ".log", MIB, now.AddDays(-30));

        int deleted = new LogDirectoryCleaner(layout, 2, NullLogger.Instance).Clean();

        Assert.Equal(2, deleted);
        Assert.True(File.Exists(active));
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(older));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void RolledSegmentsOfCurrentRun_KeepOnlyNewest()
    {
        LogFileLayout layout = LogFileLayout.Create(_baseDirectory, new DateTime(2026, 10, 18, 12, 0, 0));
        DateTime now = DateTime.UtcNow;
        string firstSegment  = WriteFile(layout, layout.RunPrefix + ".log", MIB, now.AddHours(-2));
        string activeSegment = WriteFile(layout, layout.RunPrefix + "_001.log", MIB, now);

        new LogDirectoryCleaner(layout, 1, NullLogger.Instance).Clean();

        Assert.False(File.Exists(firstSegment));
        Assert.True(File.Exists(activeSegment));
    }

    [Fact]
    public void Create_SameSecondTwice_DoesNotReuseName()
    {
        DateTime start = new(2026, 10, 18, 12, 0, 0);
        LogFileLayout first = LogFileLayout.Create(_baseDirectory, start);
        File.WriteAllText(first.FirstFilePath, "x");

        LogFileLayout second = LogFileLayout.Create(_baseDirectory, start);

        Assert.NotEqual(first.FirstFilePath, second.FirstFilePath);
    }

    private static string WriteFile(LogFileLayout layout, string name, int size, DateTime lastWriteUtc)
    {
        string path = Path.Combine(layout.Directory, name);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }
}
