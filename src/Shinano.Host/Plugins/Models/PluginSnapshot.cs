namespace Shinano.Host.Plugins.Models;

/// <summary>插件状态的只读快照。</summary>
public sealed class PluginSnapshot
{
    /// <summary>插件 ID。</summary>
    public required string Id { get; init; }

    /// <summary>显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>版本。</summary>
    public required string Version { get; init; }

    /// <summary>描述。</summary>
    public required string Description { get; init; }

    /// <summary>是否处于启用状态（已启用且初始化成功）。</summary>
    public required bool Enabled { get; init; }

    /// <summary>顺序。</summary>
    public required int SortOrder { get; init; }

    /// <summary>失败原因；正常时为 null。</summary>
    public string? Error { get; init; }

    /// <summary>是否声明了配置表单。</summary>
    public required bool HasConfig { get; init; }

    /// <summary>启用中的专有页面标题；没有时为 null。</summary>
    public string? PageTitle { get; init; }

    /// <summary>启用中的专有页面入口，相对插件包 <c>web/</c>；没有时为 null。</summary>
    public string? PageEntry { get; init; }

    /// <summary>指令类型级状态；插件首次启用前为空。</summary>
    public required IReadOnlyList<PluginCommandTypeSnapshot> CommandTypes { get; init; }
}
