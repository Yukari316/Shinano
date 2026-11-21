using Sora.Core.Types;

namespace Shinano.Framework.Commands;

/// <summary>
/// 滑动窗口限流计数：任意连续的 <c>window</c> 内最多放行 <c>limit</c> 次，线程安全，只保存在内存中。
/// 全局额度与各会话的额度互相独立：群会话按群号计数，私聊（好友与临时会话）按对方 QQ 计数。
/// 被拒绝的触发不计入额度，持续刷屏不会延长其他人的等待。
/// </summary>
public sealed class RateLimitTracker
{
    private readonly int          _limit;
    private readonly TimeSpan     _window;
    private readonly TimeProvider _time;
    private readonly Lock         _lock = new();

    // 每个队列只保存窗口内放行的时间，最多 _limit 个。
    private readonly Queue<DateTimeOffset>                      _global   = new();
    private readonly Dictionary<GroupId, Queue<DateTimeOffset>> _groups   = [];
    private readonly Dictionary<UserId, Queue<DateTimeOffset>>  _privates = [];

    /// <summary>创建限流计数。</summary>
    /// <param name="limit">窗口内最多放行的次数，至少为 1。</param>
    /// <param name="window">窗口长度，必须为正。</param>
    /// <param name="time">时间来源。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit" /> 小于 1 或 <paramref name="window" /> 不为正。</exception>
    public RateLimitTracker(int limit, TimeSpan window, TimeProvider time)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);
        _limit  = limit;
        _window = window;
        _time   = time;
    }

    /// <summary>在全局额度内放行一次；额度已满时返回 false，本次不计入。</summary>
    public bool TryAcquire()
    {
        lock (_lock)
            return TryAcquire(_global);
    }

    /// <summary>在群会话的额度内放行一次；额度已满时返回 false，本次不计入。</summary>
    public bool TryAcquire(GroupId groupId)
    {
        lock (_lock)
            return TryAcquire(QueueOf(_groups, groupId));
    }

    /// <summary>在私聊会话（对方 QQ）的额度内放行一次；额度已满时返回 false，本次不计入。</summary>
    public bool TryAcquire(UserId userId)
    {
        lock (_lock)
            return TryAcquire(QueueOf(_privates, userId));
    }

    private bool TryAcquire(Queue<DateTimeOffset> passed)
    {
        DateTimeOffset now = _time.GetUtcNow();
        while (passed.Count > 0 && now - passed.Peek() >= _window)
            passed.Dequeue();
        if (passed.Count >= _limit) return false;
        passed.Enqueue(now);
        return true;
    }

    private static Queue<DateTimeOffset> QueueOf<TKey>(Dictionary<TKey, Queue<DateTimeOffset>> sessions, TKey key) where TKey : notnull
    {
        if (!sessions.TryGetValue(key, out Queue<DateTimeOffset>? passed))
        {
            passed = new Queue<DateTimeOffset>();
            sessions[key] = passed;
        }

        return passed;
    }
}
