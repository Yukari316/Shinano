using Microsoft.Extensions.Time.Testing;
using Shinano.Framework.Commands;

namespace Shinano.Tests.Framework;

public sealed class CooldownTrackerTests
{
    [Fact]
    public void Trigger_RestartsWithinCooldown_AndRecordsAfterExpiry()
    {
        FakeTimeProvider time = new();
        CooldownTracker cooldown = new(time);
        TimeSpan window = TimeSpan.FromSeconds(20);

        Assert.False(cooldown.Trigger(1, 2, window));
        time.Advance(TimeSpan.FromSeconds(15));
        Assert.True(cooldown.Trigger(1, 2, window));
        time.Advance(TimeSpan.FromSeconds(15));
        Assert.True(cooldown.Trigger(1, 2, window));
        time.Advance(TimeSpan.FromSeconds(21));
        Assert.False(cooldown.Trigger(1, 2, window));
        // 冷却过期后的这次触发同样被记录，紧接着再触发仍处于冷却。
        Assert.True(cooldown.Trigger(1, 2, window));
        Assert.False(cooldown.Trigger(1, 3, window));
    }

    [Fact]
    public void IsCooling_OnlyChecks_AndRecordStartsCooldown()
    {
        FakeTimeProvider time = new();
        CooldownTracker cooldown = new(time);
        TimeSpan window = TimeSpan.FromSeconds(20);

        Assert.False(cooldown.IsCooling(1, 2, window));
        Assert.False(cooldown.IsCooling(1, 2, window));
        cooldown.Record(1, 2);
        time.Advance(TimeSpan.FromSeconds(19));
        Assert.True(cooldown.IsCooling(1, 2, window));
        Assert.False(cooldown.IsCooling(1, 3, window));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.False(cooldown.IsCooling(1, 2, window));
    }
}
