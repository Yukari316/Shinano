using Sora.Core.Types;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>
/// 按发言者跨会话全局计时的冷却器：冷却内再次触发不重置计时，调用失败可退还本次冷却。
/// 只保存在内存中，线程安全；Bot Super User 不豁免。
/// </summary>
public sealed class GeminiCooldownTracker
{
    private readonly Lock                             _sync = new();
    private readonly Dictionary<UserId, DateTimeOffset> _last = [];
    private readonly TimeProvider                     _time;

    /// <summary>创建冷却器。</summary>
    public GeminiCooldownTracker(TimeProvider time) => _time = time;

    /// <summary>
    /// 尝试占用发言者的一次冷却窗口。冷却时长不大于 0 时直接放行。
    /// </summary>
    public bool TryAcquire(UserId userId, TimeSpan cooldown, out CooldownTicket ticket, out int remainingSeconds)
    {
        if (cooldown <= TimeSpan.Zero)
        {
            ticket = default;
            remainingSeconds = 0;
            return true;
        }

        DateTimeOffset now = _time.GetUtcNow();
        lock (_sync)
        {
            if (_last.TryGetValue(userId, out DateTimeOffset previous))
            {
                TimeSpan elapsed = now - previous;
                if (elapsed < cooldown)
                {
                    ticket = default;
                    remainingSeconds = Math.Max(1, (int)Math.Ceiling((cooldown - elapsed).TotalSeconds));
                    return false;
                }

                _last[userId] = now;
                ticket = new CooldownTicket
                {
                    UserId     = userId,
                    AcquiredAt = now,
                    Previous   = previous,
                    Active     = true
                };
                remainingSeconds = 0;
                return true;
            }

            _last[userId] = now;
            ticket = new CooldownTicket
            {
                UserId     = userId,
                AcquiredAt = now,
                Previous   = null,
                Active     = true
            };
            remainingSeconds = 0;
            return true;
        }
    }

    /// <summary>请求失败时退还本次冷却；若期间已有更新的成功触发则不覆盖。</summary>
    public void Refund(CooldownTicket ticket)
    {
        if (!ticket.Active) return;

        lock (_sync)
        {
            if (!_last.TryGetValue(ticket.UserId, out DateTimeOffset current) || current != ticket.AcquiredAt)
                return;

            if (ticket.Previous is { } previous) _last[ticket.UserId] = previous;
            else _last.Remove(ticket.UserId);
        }
    }
}

/// <summary>一次已放行的冷却凭据，用于调用失败时退还。</summary>
public readonly struct CooldownTicket
{
    /// <summary>发言者。</summary>
    public UserId UserId { get; init; }

    /// <summary>本次记录的触发时间。</summary>
    public DateTimeOffset AcquiredAt { get; init; }

    /// <summary>本次之前的上次触发时间。</summary>
    public DateTimeOffset? Previous { get; init; }

    /// <summary>是否需要退还。</summary>
    public bool Active { get; init; }
}
