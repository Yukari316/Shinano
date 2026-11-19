using PuppeteerSharp;

namespace Shinano.Framework.Plugins;

/// <summary>
/// 一次浏览器会话：持有一个浏览器进程，释放时关闭进程。用户目录在会话之间保留，Cookie 与 localStorage 随之持久化。
/// 不是线程安全的，由打开它的调用方独占使用。
/// </summary>
public interface IBrowserSession : IAsyncDisposable
{
    /// <summary>会话的首个页面，已应用 UA 与 Client Hints 覆盖。</summary>
    IPage Page { get; }

    /// <summary>浏览器对外呈现的身份；插件的 HTTP 请求应使用同一组请求头，避免与浏览器不一致。</summary>
    BrowserIdentity Identity { get; }

    /// <summary>新开一个页面并应用同样的覆盖。</summary>
    Task<IPage> NewPageAsync();
}

/// <summary>浏览器对外呈现的身份，即每个请求都会带的 UA 与低熵 Client Hints 请求头的值。</summary>
public sealed class BrowserIdentity
{
    /// <summary><c>User-Agent</c>。</summary>
    public required string UserAgent { get; init; }

    /// <summary><c>sec-ch-ua</c>，如 <c>"Not-A.Brand";v="99", "Google Chrome";v="157", "Chromium";v="157"</c>。</summary>
    public required string SecChUa { get; init; }

    /// <summary><c>sec-ch-ua-mobile</c>，如 <c>?0</c>。</summary>
    public required string SecChUaMobile { get; init; }

    /// <summary><c>sec-ch-ua-platform</c>，如 <c>"Windows"</c>。</summary>
    public required string SecChUaPlatform { get; init; }

    /// <summary><c>Accept-Language</c>。</summary>
    public required string AcceptLanguage { get; init; }
}
