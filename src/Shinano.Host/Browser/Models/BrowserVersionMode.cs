namespace Shinano.Host.Browser.Models;

/// <summary>宿主浏览器（Chrome for Testing）的版本选择。</summary>
public enum BrowserVersionMode
{
    /// <summary>最新稳定版：启动浏览器时解析，每天最多一次，有新版即下载并删除旧版。</summary>
    Latest,

    /// <summary>PuppeteerSharp 固定的版本，与库的 CDP 实现保证兼容。</summary>
    Pinned,

    /// <summary>指定主版本号下最新的构建。</summary>
    Specific
}
