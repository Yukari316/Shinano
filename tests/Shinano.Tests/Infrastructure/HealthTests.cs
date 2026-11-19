using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Infrastructure.Health;

namespace Shinano.Tests.Infrastructure;

public sealed class HealthTests
{
    [Fact]
    public void Overall_IsWorstComponent_AndChangeFiresOnlyOnLevelChange()
    {
        RuntimeHealth health = new(new FakeTimeProvider());
        List<HealthChange> changes = [];
        health.Changed += changes.Add;

        health.Set(HealthComponent.ObjectStorage, HealthLevel.Degraded, "a");
        health.Set(HealthComponent.ObjectStorage, HealthLevel.Degraded, "b");
        Assert.Equal(HealthLevel.Degraded, health.Overall);

        health.Set(HealthComponent.Database, HealthLevel.Stopped, "down");
        Assert.Equal(HealthLevel.Stopped, health.Overall);

        health.Set(HealthComponent.Database, HealthLevel.Normal, string.Empty);
        Assert.Equal(HealthLevel.Degraded, health.Overall);
        Assert.Equal(3, changes.Count);
    }

    [Fact]
    public async Task Probe_IsThrottled_UnlessForced()
    {
        FakeTimeProvider time = new();
        RuntimeHealth health = new(time);
        int probes = 0;
        bool reachable = false;
        DependencyAvailability availability = new(
            HealthComponent.Database,
            HealthLevel.Stopped,
            _ =>
            {
                probes++;
                return reachable ? Task.CompletedTask : Task.FromException(new IOException("refused"));
            },
            TimeSpan.FromSeconds(30),
            health,
            time,
            NullLogger.Instance);
        CancellationToken token = TestContext.Current.CancellationToken;

        availability.ReportFailure(new IOException("refused"));
        Assert.False(await availability.CheckAsync(force: false, token));
        Assert.Equal(0, probes);

        time.Advance(TimeSpan.FromSeconds(31));
        Assert.False(await availability.CheckAsync(force: false, token));
        Assert.Equal(1, probes);

        reachable = true;
        Assert.True(await availability.CheckAsync(force: true, token));
        Assert.Equal(2, probes);
        Assert.Equal(HealthLevel.Normal, health.Overall);
    }
}
