namespace Shinano.Host.Browser.Models;

/// <summary>宿主浏览器服务的部署配置，位于 <c>Shinano:Browser</c>，重启生效。</summary>
public sealed class BrowserOptions
{
    /// <summary>配置节名。</summary>
    public const string SECTION_NAME = "Shinano:Browser";

    /// <summary>版本选择；配置了 <see cref="ExecutablePath" /> 时不使用。</summary>
    public BrowserVersionMode VersionMode { get; set; } = BrowserVersionMode.Latest;

    /// <summary><see cref="BrowserVersionMode.Specific" /> 时的主版本号，如 157。</summary>
    public int Version { get; set; }

    /// <summary>本机浏览器可执行文件路径，相对路径基于部署数据目录；为空时下载 Chrome for Testing。</summary>
    public string? ExecutablePath { get; set; }
}
