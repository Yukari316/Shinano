namespace Shinano.Host.Plugins;

/// <summary>插件状态的只读快照。</summary>
public sealed class PluginSnapshot
{
    /// <summary>插件 ID。</summary>
    public required string Id { get; init; }

    /// <summary>显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>是否启用。</summary>
    public required bool Enabled { get; init; }

    /// <summary>顺序。</summary>
    public required int SortOrder { get; init; }

    /// <summary>失败原因；正常时为 null。</summary>
    public string? Error { get; init; }
}
