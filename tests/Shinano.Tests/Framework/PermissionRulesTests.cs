using Shinano.Framework.Commands;
using Sora.Core.Enums;

namespace Shinano.Tests.Framework;

public sealed class PermissionRulesTests
{
    [Theory]
    [InlineData(false, MessageSourceType.Group, MemberRole.Member, MemberRole.Member, true)]
    [InlineData(false, MessageSourceType.Group, MemberRole.Member, MemberRole.Admin, false)]
    [InlineData(false, MessageSourceType.Group, MemberRole.Admin, MemberRole.Admin, true)]
    [InlineData(false, MessageSourceType.Group, MemberRole.Owner, MemberRole.Admin, true)]
    [InlineData(false, MessageSourceType.Group, MemberRole.Admin, MemberRole.Owner, false)]
    [InlineData(true, MessageSourceType.Group, MemberRole.Member, MemberRole.Owner, true)]
    [InlineData(false, MessageSourceType.Friend, MemberRole.Unknown, MemberRole.Member, true)]
    [InlineData(false, MessageSourceType.Friend, MemberRole.Unknown, MemberRole.Admin, false)]
    [InlineData(true, MessageSourceType.Friend, MemberRole.Unknown, MemberRole.Admin, true)]
    public void IsAllowed_FollowsShinanoRules(
        bool              isSuperUser,
        MessageSourceType source,
        MemberRole        role,
        MemberRole        required,
        bool              expected) =>
        Assert.Equal(expected, PermissionRules.IsAllowed(isSuperUser, source, role, required));
}
