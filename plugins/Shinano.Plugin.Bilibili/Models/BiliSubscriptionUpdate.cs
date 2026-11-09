namespace Shinano.Plugin.Bilibili.Models;

/// <summary>面板保存一个 UP 的推送设置。</summary>
public sealed class BiliSubscriptionUpdate
{
    /// <summary>是否推送动态。</summary>
    public bool PushDynamic { get; init; }

    /// <summary>是否推送直播。</summary>
    public bool PushLive { get; init; }

    /// <summary>推送的群。</summary>
    public IReadOnlyList<long> Groups { get; init; } = [];
}
