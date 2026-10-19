using Sora.Core.Enums;

namespace Shinano.Framework.Permissions;

/// <summary>
/// Shinano 的指令权限规则：Super User 先于 IM 角色放行；群聊比较成员角色；
/// 私聊没有 IM 角色，要求高于 Member 时只有 Super User 可执行。
/// </summary>
public static class PermissionRules
{
    /// <summary>判断是否允许执行。</summary>
    /// <param name="isSuperUser">触发者是否为部署配置的 Super User。</param>
    /// <param name="sourceType">消息来源。</param>
    /// <param name="memberRole">群聊中触发者的成员角色；私聊忽略。</param>
    /// <param name="required">要求的最低角色。</param>
    public static bool IsAllowed(bool isSuperUser, MessageSourceType sourceType, MemberRole memberRole, MemberRole required)
    {
        if (isSuperUser || required <= MemberRole.Member) return true;
        return sourceType == MessageSourceType.Group && memberRole >= required;
    }
}
