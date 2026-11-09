namespace Shinano.Plugin.Bilibili.Models;

/// <summary>面板显示的登录状态。</summary>
public sealed class BiliStatusView
{
    /// <summary>登录状态。</summary>
    public required BiliLoginState State { get; init; }

    /// <summary>已登录的账号；未登录时为 null。</summary>
    public BiliAccount? Account { get; init; }

    /// <summary>风控暂停结束的时间；不在暂停中时为 null。</summary>
    public DateTimeOffset? RiskUntil { get; init; }
}
