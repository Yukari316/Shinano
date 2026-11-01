namespace Shinano.Host.Plugins.Models;

/// <summary>一项插件包变更的内容。</summary>
public sealed class PluginChange
{
    /// <summary>变更类型。</summary>
    public required PluginChangeKind Kind { get; init; }

    /// <summary>插件 ID。</summary>
    public required string PluginId { get; init; }

    /// <summary>显示名称；删除时取已安装插件的名称。</summary>
    public required string Name { get; init; }

    /// <summary>已安装的版本；安装新插件时为 null。</summary>
    public string? FromVersion { get; init; }

    /// <summary>新插件包的版本；删除时为 null。</summary>
    public string? ToVersion { get; init; }
}
