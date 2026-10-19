using Microsoft.Extensions.Hosting;

namespace Shinano.Infrastructure.Logging;

/// <summary>启动时及之后每小时执行一次日志目录清理。</summary>
public sealed class LogRetentionService : BackgroundService
{
    private static readonly TimeSpan CleanInterval = TimeSpan.FromHours(1);

    private readonly LogDirectoryCleaner _cleaner;

    /// <summary>创建服务。</summary>
    public LogRetentionService(LogDirectoryCleaner cleaner)
    {
        _cleaner = cleaner;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new(CleanInterval);
        do
        {
            _cleaner.Clean();
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async ValueTask<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
