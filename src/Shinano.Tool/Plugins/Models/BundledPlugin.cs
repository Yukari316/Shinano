namespace Shinano.Tool.Plugins.Models;

/// <summary>发布包 <c>bundled-plugins/</c> 中的一个插件包。</summary>
public sealed class BundledPlugin
{
    /// <summary>插件包 zip 的路径。</summary>
    public required string ArchivePath { get; init; }

    /// <summary>插件包中的清单。</summary>
    public required PluginManifest Manifest { get; init; }
}
