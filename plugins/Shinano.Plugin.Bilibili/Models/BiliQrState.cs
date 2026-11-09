namespace Shinano.Plugin.Bilibili.Models;

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
