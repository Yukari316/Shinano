namespace Shinano.Host.Plugins.Staging;

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

    /// <summary>删除时是否同时清除插件数据；清除在重启确认成功后执行。只用于删除。</summary>
    public bool PurgeData { get; init; }
}

/// <summary>插件包变更的类型。</summary>
public enum PluginChangeKind
{
    /// <summary>安装新插件。</summary>
    Install,

    /// <summary>以新的插件包替换已安装的同 ID 插件，允许降级。</summary>
    Upgrade,

    /// <summary>删除插件包；插件数据保留，除非变更选择了 <see cref="PluginChange.PurgeData" />。</summary>
    Remove
}
