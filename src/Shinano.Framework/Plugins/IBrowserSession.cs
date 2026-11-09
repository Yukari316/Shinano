using PuppeteerSharp;
using Shinano.Framework.Plugins.Models;

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
