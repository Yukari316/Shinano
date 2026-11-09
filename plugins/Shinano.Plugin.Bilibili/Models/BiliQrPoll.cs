using System.Net;

namespace Shinano.Plugin.Bilibili.Models;

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
