using Sora.Command.Filters;
using Sora.Command.InternalEntities;
using Sora.Core.Enums;
using Sora.Entities.Events;

namespace Shinano.Framework.Commands;

/// <summary>
/// 指令权限过滤器。Sora 的 <c>PermissionLevel</c> 不让 Super User 绕过群角色检查，
/// 因此 Shinano 指令在 <c>[Command]</c> 上保持 Member 级，改用本特性声明要求的角色。
/// 拒绝时不回复；指令已命中，消息被占用。规则见 <see cref="PermissionRules" />。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequirePermissionAttribute : CommandBeforeFilterAttribute
{
    /// <summary>创建过滤器。</summary>
    /// <param name="required">要求的最低成员角色。</param>
    public RequirePermissionAttribute(MemberRole required)
    {
        Required = required;
    }

    /// <summary>要求的最低成员角色。</summary>
    public MemberRole Required { get; }

    /// <inheritdoc />
    public override ValueTask<bool> OnBeforeExecuteAsync(
        MessageReceivedEvent e,
        CommandFilterContext cmd,
        CancellationToken    ct) =>
        ValueTask.FromResult(PermissionRules.IsAllowed(e.IsSuperUser, e.Message.SourceType, e.Member.Role, Required));
}
