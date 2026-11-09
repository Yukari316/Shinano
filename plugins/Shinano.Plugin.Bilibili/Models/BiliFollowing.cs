namespace Shinano.Plugin.Bilibili.Models;

/// <summary>关注列表中的一个 UP。</summary>
public sealed class BiliFollowing
{
    /// <summary>UID。</summary>
    public required long Uid { get; init; }

    /// <summary>昵称。</summary>
    public required string Name { get; init; }

    /// <summary>头像地址。</summary>
    public required string Face { get; init; }
}
