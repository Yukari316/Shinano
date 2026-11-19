using Shinano.Framework.Plugins;

namespace Shinano.Plugin.Bilibili.Login;

/// <summary>从浏览器导出、供 HTTP 读取使用的登录 Cookie 与浏览器身份。</summary>
public sealed class BiliCredentials
{
    /// <summary><c>Cookie</c> 请求头的值（B站各域名共用的全部 Cookie）。</summary>
    public required string CookieHeader { get; init; }

    /// <summary>CSRF 令牌（Cookie <c>bili_jct</c>）；未登录时为空。</summary>
    public required string Csrf { get; init; }

    /// <summary>浏览器的 UA 与 Client Hints。</summary>
    public required BrowserIdentity Identity { get; init; }
}
