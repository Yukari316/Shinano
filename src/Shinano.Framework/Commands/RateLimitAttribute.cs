using Sora.Command.Filters;
using Sora.Command.InternalEntities;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Framework.Commands;

/// <summary>
/// 指令限流过滤器：任意连续 <see cref="Window" /> 内最多放行 <see cref="Count" /> 次，规则见 <see cref="RateLimitTracker" />；
/// 默认全部来源共用一份额度，<see cref="Scope" /> 为 <see cref="RateLimitScope.Session" /> 时按会话计数。Super User 不豁免。
/// 标在方法上时该指令单独计数，标在指令组类上时整组共享额度（Sora 按特性实例缓存过滤器）。
/// 额度已满时拒绝执行：指令已命中，消息被占用；设置了 <see cref="Reply" /> 时回复该文字，否则不回复。
/// 排在 <see cref="CooldownAttribute" /> 之后，冷却中的调用不消耗额度；被拒绝的调用不计入。
/// 反过来，与 <see cref="CooldownAttribute" /> 同时使用时，冷却在本过滤器之前已经开始计时，被限流拒绝的调用同样进入冷却。
/// 计数只在内存中，插件停用再启用时保留，进程重启后清空。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RateLimitAttribute : CommandBeforeFilterAttribute
{
    // CooldownAttribute 为 100。
    private const int ORDER = 110;

    private readonly RateLimitTracker _tracker;

    /// <summary>创建过滤器。</summary>
    /// <param name="count">窗口内最多放行的次数，至少为 1。</param>
    /// <param name="seconds">窗口秒数，必须为正。</param>
    public RateLimitAttribute(int count, int seconds)
    {
        Count    = count;
        Window   = TimeSpan.FromSeconds(seconds);
        _tracker = new RateLimitTracker(count, Window, TimeProvider.System);
    }

    /// <summary>窗口内最多放行的次数。</summary>
    public int Count { get; }

    /// <summary>窗口长度。</summary>
    public TimeSpan Window { get; }

    /// <summary>计数范围，默认全局。</summary>
    public RateLimitScope Scope { get; set; } = RateLimitScope.Global;

    /// <summary>额度已满时回复的文字；为空时不回复。</summary>
    public string Reply { get; set; } = string.Empty;

    /// <inheritdoc />
    public override int Order => ORDER;

    /// <inheritdoc />
    public override async ValueTask<bool> OnBeforeExecuteAsync(
        MessageReceivedEvent e,
        CommandFilterContext cmd,
        CancellationToken    ct)
    {
        bool passed = Scope switch
        {
            RateLimitScope.Session when e.Message.SourceType == MessageSourceType.Group => _tracker.TryAcquire(e.Message.GroupId),
            RateLimitScope.Session                                                      => _tracker.TryAcquire(e.Message.SenderId),
            _                                                                           => _tracker.TryAcquire()
        };
        if (passed) return true;

        if (Reply.Length > 0) await e.ReplyAsync(new MessageBody(Reply), ct);
        return false;
    }
}

/// <summary><see cref="RateLimitAttribute" /> 的计数范围。</summary>
public enum RateLimitScope
{
    /// <summary>全部来源共用一份额度。</summary>
    Global,

    /// <summary>按会话计数：群聊按群号，私聊（好友与临时会话）按对方 QQ。</summary>
    Session
}
