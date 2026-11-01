using System.Collections.Concurrent;
using Sora.Core.Types;

namespace Shinano.Framework.Commands;

/// <summary>
/// 按群与发送者记录触发时间的冷却计时，线程安全，只保存在内存中。
/// 冷却内再次触发会重新计时（沿用旧版行为，持续刷屏的人一直处于冷却中）；冷却外触发记录本次时间。
/// </summary>
public sealed class CooldownTracker
{
    private readonly ConcurrentDictionary<(GroupId GroupId, UserId UserId), DateTimeOffset> _lastTriggered = new();
    private readonly TimeProvider                                                          _time;

    /// <summary>创建冷却计时。</summary>
    public CooldownTracker(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>记录一次触发并返回本次是否处于冷却中。</summary>
    /// <param name="groupId">群号；私聊不区分来源群，传入 <c>default</c>，只按发送者计时。</param>
    /// <param name="userId">发送者。</param>
    /// <param name="cooldown">冷却时长。</param>
    public bool Trigger(GroupId groupId, UserId userId, TimeSpan cooldown)
    {
        DateTimeOffset now        = _time.GetUtcNow();
        bool           inCooldown = false;
        _lastTriggered.AddOrUpdate(
            (groupId, userId),
            now,
            (_, last) =>
            {
                inCooldown = now - last < cooldown;
                return now;
            });
        return inCooldown;
    }
}
