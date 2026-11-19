using System.Net;

namespace Shinano.Plugin.Bilibili.Login;

/// <summary>一次扫码结果查询。</summary>
public sealed class BiliQrPoll
{
    /// <summary>进度；成功时为 <see cref="BiliQrState.Succeeded" />，但登录状态尚未写入浏览器。</summary>
    public required BiliQrState State { get; init; }

    /// <summary>登录成功时下发的 Cookie。</summary>
    public IReadOnlyList<Cookie> Cookies { get; init; } = [];

    /// <summary>登录成功时下发的刷新令牌，网页前端用它刷新 Cookie。</summary>
    public string RefreshToken { get; init; } = "";
}

/// <summary>扫码登录的进度。</summary>
public enum BiliQrState
{
    /// <summary>等待扫码。</summary>
    Waiting,

    /// <summary>已扫码，等待在手机上确认。</summary>
    Scanned,

    /// <summary>登录成功，已写入浏览器。</summary>
    Succeeded,

    /// <summary>二维码已过期。</summary>
    Expired,

    /// <summary>接口或浏览器出错。</summary>
    Failed
}

/// <summary>扫码登录的二维码。</summary>
public sealed class BiliQrCode
{
    /// <summary>轮询扫码结果用的键。</summary>
    public required string Key { get; init; }

    /// <summary>二维码 PNG。</summary>
    public required byte[] Png { get; init; }
}
