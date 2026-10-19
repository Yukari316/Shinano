using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Infrastructure.Scheduling;

namespace Shinano.Tests.Scheduling;

public sealed class HostSchedulerTests
{
    [Fact]
    public async Task DisabledOwnerAndPause_SkipTriggers()
    {
        FakeTimeProvider time = new();
        await using HostScheduler scheduler = new(NullLogger<HostScheduler>.Instance, time);
        int runs = 0;
        SemaphoreSlim ran = new(0);
        scheduler.Register(
            "plugin",
            "job",
            TimeSpan.FromMinutes(1),
            _ =>
            {
                Interlocked.Increment(ref runs);
                ran.Release();
                return Task.CompletedTask;
            },
            runImmediately: false);
        CancellationToken token = TestContext.Current.CancellationToken;

        await TickAsync(time);
        Assert.True(await ran.WaitAsync(TimeSpan.FromSeconds(5), token));

        scheduler.SetOwnerEnabled("plugin", false);
        await TickAsync(time);
        scheduler.SetOwnerEnabled("plugin", true);
        scheduler.Paused = true;
        await TickAsync(time);
        Assert.Equal(1, runs);

        scheduler.Paused = false;
        await TickAsync(time);
        Assert.True(await ran.WaitAsync(TimeSpan.FromSeconds(5), token));
        Assert.Equal(2, runs);
        await scheduler.StopAsync(token);
    }

    private static async Task TickAsync(FakeTimeProvider time)
    {
        // 给循环任务时间进入等待后再推进时钟。
        await Task.Delay(50, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(1));
        await Task.Delay(50, TestContext.Current.CancellationToken);
    }
}
