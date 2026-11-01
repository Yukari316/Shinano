using Sora.Command.Filters;
using Sora.Command.InternalEntities;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Framework.Commands;

/// <summary>
/// 指令冷却过滤器，按群与发送者计时，规则见 <see cref="CooldownTracker" />；Super User 不豁免。
/// 标在方法上时该指令单独冷却，标在指令组类上时整组共享冷却（Sora 按特性实例缓存过滤器）。
/// 冷却中拒绝执行：指令已命中，消息被占用；设置了 <see cref="Reply" /> 时回复该文字，否则不回复。
/// 排在 <c>RequirePermission</c> 等默认顺序的过滤器之后，被拒绝的调用不计入冷却。
/// 计时只在内存中，插件停用再启用时保留，进程重启后清空。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class CooldownAttribute : CommandBeforeFilterAttribute
{
    private const int ORDER = 100;

    private readonly CooldownTracker _tracker = new(TimeProvider.System);

    /// <summary>创建过滤器。</summary>
    /// <param name="seconds">冷却秒数。</param>
    public CooldownAttribute(int seconds)
    {
        Cooldown = TimeSpan.FromSeconds(seconds);
    }

    /// <summary>冷却时长。</summary>
    public TimeSpan Cooldown { get; }

    /// <summary>冷却中被触发时回复的文字；为空时不回复。</summary>
    public string Reply { get; set; } = string.Empty;

    /// <inheritdoc />
    public override int Order => ORDER;

    /// <inheritdoc />
    public override async ValueTask<bool> OnBeforeExecuteAsync(
        MessageReceivedEvent e,
        CommandFilterContext cmd,
        CancellationToken    ct)
    {
        GroupId groupId = e.Message.SourceType == MessageSourceType.Group ? e.Message.GroupId : default;
        if (!_tracker.Trigger(groupId, e.Message.SenderId, Cooldown)) return true;

        if (Reply.Length > 0) await e.ReplyAsync(new MessageBody(Reply), ct);
        return false;
    }
}
