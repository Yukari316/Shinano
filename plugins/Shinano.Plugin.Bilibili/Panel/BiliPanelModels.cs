using Shinano.Plugin.Bilibili.Login;

namespace Shinano.Plugin.Bilibili.Panel;

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

/// <summary>面板可选的推送群。</summary>
public sealed class BiliGroupView
{
    /// <summary>群号。</summary>
    public required long Id { get; init; }

    /// <summary>群名；Bot 已不在该群时为 null。</summary>
    public string? Name { get; init; }
}

/// <summary>面板显示的二维码。</summary>
public sealed class BiliQrView
{
    /// <summary>轮询扫码结果用的键。</summary>
    public required string Key { get; init; }

    /// <summary>二维码 PNG 的 data URL。</summary>
    public required string Image { get; init; }
}

/// <summary>面板轮询的扫码进度。</summary>
public sealed class BiliQrStateView
{
    /// <summary>进度。</summary>
    public required BiliQrState State { get; init; }

    /// <summary>登录成功后的账号；其他状态为 null。</summary>
    public BiliAccount? Account { get; init; }
}

/// <summary>面板发起扫码登录。</summary>
public sealed class BiliLoginRequest
{
    /// <summary>已有登录时是否确认退出原账号；为 false 且已登录时不生成二维码，返回原账号供确认。</summary>
    public bool ConfirmLogout { get; init; }
}

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
