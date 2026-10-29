namespace Shinano.Tool.Plugins.Models;

/// <summary>插件目录中已安装的一个插件包。</summary>
public sealed class InstalledPlugin
{
    /// <summary>插件包目录。</summary>
    public required string Directory { get; init; }

    /// <summary>清单。</summary>
    public required PluginManifest Manifest { get; init; }
}
