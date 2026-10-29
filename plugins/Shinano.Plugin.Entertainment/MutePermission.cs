using Sora.Core.Enums;

namespace Shinano.Plugin.Entertainment;

/// <summary>
/// QQ 群禁言的权限规则：群主可以禁言管理员与普通成员，管理员只能禁言普通成员，群主不能被禁言。
/// LLBot 对没有权限、实际未生效的禁言同样返回成功，所以在调用前按双方角色判断。
/// </summary>
public static class MutePermission
{
    /// <summary>Bot 能否禁言该成员。</summary>
    /// <param name="bot">Bot 在群中的角色。</param>
    /// <param name="target">被禁言成员的角色。</param>
    public static bool CanMute(MemberRole bot, MemberRole target) =>
        bot switch
        {
            MemberRole.Owner => target is MemberRole.Admin or MemberRole.Member,
            MemberRole.Admin => target == MemberRole.Member,
            _                => false
        };
}
