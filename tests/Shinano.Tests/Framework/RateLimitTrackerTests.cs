using Microsoft.Extensions.Time.Testing;
using Shinano.Framework.Commands;
using Sora.Core.Types;

namespace Shinano.Tests.Framework;

public sealed class RateLimitTrackerTests
{
    private const int LIMIT = 2;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    [Fact]
    public void TryAcquire_SlidingWindow_DoesNotCountRejected()
    {
        FakeTimeProvider time = new();
        RateLimitTracker limit = new(LIMIT, Window, time);

        Assert.True(limit.TryAcquire());
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(limit.TryAcquire());
        Assert.False(limit.TryAcquire());
        // 第一次放行满 10 秒后让出一个名额；期间被拒绝的触发不计入，不会推迟让出的时间。
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.True(limit.TryAcquire());
        Assert.False(limit.TryAcquire());
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.True(limit.TryAcquire());
    }

    [Fact]
    public void TryAcquire_SessionsAndGlobal_HaveSeparateQuotas()
    {
        RateLimitTracker limit = new(1, Window, new FakeTimeProvider());

        Assert.True(limit.TryAcquire(new GroupId(1)));
        Assert.False(limit.TryAcquire(new GroupId(1)));
        Assert.True(limit.TryAcquire(new GroupId(2)));
        // 同一数值的群号与 QQ 号是不同会话。
        Assert.True(limit.TryAcquire(new UserId(1)));
        Assert.False(limit.TryAcquire(new UserId(1)));
        Assert.True(limit.TryAcquire());
    }
}
