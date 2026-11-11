namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>从 PyPI 安装 NB2 插件的请求。</summary>
public sealed class Nb2InstallRequest
{
    /// <summary>PyPI 包名。</summary>
    public string Package { get; init; } = string.Empty;

    /// <summary>版本。</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>顶层模块名；为空时取包名把 <c>-</c> 换成 <c>_</c>。</summary>
    public string? Module { get; init; }
}
