namespace Shinano.Plugin.Bilibili.Models;

/// <summary>B站登录状态。</summary>
public enum BiliLoginState
{
    /// <summary>尚未检查（插件刚启用，浏览器维护还没完成）。</summary>
    Unknown,

    /// <summary>未登录。</summary>
    LoggedOut,

    /// <summary>已登录。</summary>
    LoggedIn,

    /// <summary>曾经登录，现已失效。</summary>
    Expired
}
