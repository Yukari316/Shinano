namespace Shinano.Host.Plugins;

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

    /// <summary>持久化的启用状态；是否已在响应见 <see cref="Status" />。</summary>
    public required bool Enabled { get; init; }

    /// <summary>运行状态。</summary>
    public required PluginStatus Status { get; init; }

    /// <summary>顺序。</summary>
    public required int SortOrder { get; init; }

    /// <summary>失败原因；正常时为 null。</summary>
    public string? Error { get; init; }

    /// <summary>是否声明了配置表单。</summary>
    public required bool HasConfig { get; init; }

    /// <summary>是否声明了机密类型（插件页显示机密表单）。</summary>
    public required bool HasSecrets { get; init; }

    /// <summary>启用中的专有页面标题；没有时为 null。</summary>
    public string? PageTitle { get; init; }

    /// <summary>启用中的专有页面入口，相对插件包 <c>web/</c>；没有时为 null。</summary>
    public string? PageEntry { get; init; }

    /// <summary>指令类型级状态；插件首次启用前为空。</summary>
    public required IReadOnlyList<PluginCommandTypeSnapshot> CommandTypes { get; init; }
}

/// <summary>插件内一个指令类型的状态与指令数量。</summary>
public sealed class PluginCommandTypeSnapshot
{
    /// <summary>类型名。</summary>
    public required string Name { get; init; }

    /// <summary>指令能否匹配（程序集与类型都已启用）。</summary>
    public required bool Enabled { get; init; }

    /// <summary>指令数量。</summary>
    public required int CommandCount { get; init; }
}

/// <summary>插件当前的运行状态，供管理面板显示。</summary>
public enum PluginStatus
{
    /// <summary>未启用。</summary>
    Disabled,

    /// <summary>已启用并在响应。</summary>
    Active,

    /// <summary>已启用，等待机器人账号就绪后激活。</summary>
    Pending,

    /// <summary>已启用但初始化失败；在依赖恢复或重新启用时重试。</summary>
    InitFailed,

    /// <summary>加载失败，进程重启前不能启用。</summary>
    LoadFailed
}
