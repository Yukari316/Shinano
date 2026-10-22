namespace Shinano.Host.Plugins;

/// <summary>插件目录下一个含 <c>plugin.json</c> 的子目录。</summary>
public sealed class PluginPackageDirectory
{
    /// <summary>子目录的完整路径。</summary>
    public required string Directory { get; init; }

    /// <summary>清单；清单无效时为 null。</summary>
    public PluginManifest? Manifest { get; init; }

    /// <summary>清单无效的原因；有效时为 null。</summary>
    public string? Error { get; init; }
}
