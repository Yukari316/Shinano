namespace Shinano.Plugin.Bilibili.Models;

/// <summary>面板中关注列表的一行：UP 与其推送设置。</summary>
public sealed class BiliFollowView
{
    /// <summary>UID。</summary>
    public required long Uid { get; init; }

    /// <summary>昵称。</summary>
    public required string Name { get; init; }

    /// <summary>头像地址。</summary>
    public required string Face { get; init; }

    /// <summary>最近一条动态的发布时间；未知时为 null。</summary>
    public DateTimeOffset? LastDynamicAt { get; init; }

    /// <summary>是否推送动态。</summary>
    public required bool PushDynamic { get; init; }

    /// <summary>是否推送直播。</summary>
    public required bool PushLive { get; init; }

    /// <summary>推送的群。</summary>
    public required IReadOnlyList<long> Groups { get; init; }
}
