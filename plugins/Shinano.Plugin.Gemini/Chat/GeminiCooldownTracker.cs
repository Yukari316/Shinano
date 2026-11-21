using Sora.Core.Types;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>
/// 按发言者跨会话全局计时的冷却器：冷却从 Bot 发出回复后开始计时，冷却内再次触发不重置计时；
/// 冷却开启时，同一发言者已有请求在生成（任一会话）时拒绝新的触发，避免在多个会话同时发起而绕过冷却。
/// 调用失败时释放占用且不开始计时。只保存在内存中，线程安全；Bot Super User 不豁免。
/// </summary>
public sealed class GeminiCooldownTracker
{
    private readonly Lock                          _sync   = new();
    private readonly Dictionary<UserId, UserState> _states = [];
    private readonly TimeProvider                  _time;

    /// <summary>创建冷却器。</summary>
    public GeminiCooldownTracker(TimeProvider time) => _time = time;

    /// <summary>
    /// 尝试为发言者占用一次请求。冷却时长不大于 0 时直接放行且不占用；放行后须以
    /// <see cref="Complete" />（回复已发出）或 <see cref="Release" />（调用失败）结束占用。
    /// </summary>
    public CooldownResult TryAcquire(UserId userId, TimeSpan cooldown, out CooldownTicket ticket, out int remainingSeconds)
    {
        ticket = default;
        remainingSeconds = 0;
        if (cooldown <= TimeSpan.Zero) return CooldownResult.Acquired;

        DateTimeOffset now = _time.GetUtcNow();
        lock (_sync)
        {
            if (!_states.TryGetValue(userId, out UserState? state))
            {
                state = new UserState();
                _states[userId] = state;
            }

            if (state.InFlight) return CooldownResult.Busy;

            if (state.LastReplyAt is { } lastReply && now - lastReply < cooldown)
            {
                remainingSeconds = Math.Max(1, (int)Math.Ceiling((cooldown - (now - lastReply)).TotalSeconds));
                return CooldownResult.CoolingDown;
            }

            state.InFlight = true;
            ticket = new CooldownTicket
            {
                UserId = userId,
                Active = true
            };
            return CooldownResult.Acquired;
        }
    }

    /// <summary>Bot 已发出回复：结束占用并从现在开始计算冷却。</summary>
    public void Complete(CooldownTicket ticket) => Finish(ticket, startCooldown: true);

    /// <summary>调用失败或未发起调用：结束占用，不开始冷却。</summary>
    public void Release(CooldownTicket ticket) => Finish(ticket, startCooldown: false);

    private void Finish(CooldownTicket ticket, bool startCooldown)
    {
        if (!ticket.Active) return;

        DateTimeOffset now = _time.GetUtcNow();
        lock (_sync)
        {
            if (!_states.TryGetValue(ticket.UserId, out UserState? state)) return;
            state.InFlight = false;
            if (startCooldown) state.LastReplyAt = now;
        }
    }

    private sealed class UserState
    {
        public bool            InFlight    { get; set; }
        public DateTimeOffset? LastReplyAt { get; set; }
    }
}

/// <summary>冷却检查结果。</summary>
public enum CooldownResult
{
    /// <summary>放行。</summary>
    Acquired,

    /// <summary>该发言者已有请求在生成。</summary>
    Busy,

    /// <summary>仍在冷却中。</summary>
    CoolingDown
}

/// <summary>一次已放行的占用凭据，用于结束占用。</summary>
public readonly struct CooldownTicket
{
    /// <summary>发言者。</summary>
    public UserId UserId { get; init; }

    /// <summary>是否占用了冷却器（冷却关闭时为 false）。</summary>
    public bool Active { get; init; }
}
