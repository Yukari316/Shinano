namespace Shinano.Tool.Plugins;

/// <summary>
/// 宿主插件包暂存区 <c>plugin-staging/pending.json</c> 中的一项，格式与宿主一致：属性名为 camelCase，枚举以名称保存。
/// 工具只追加升级，不修改其他项。
/// </summary>
public sealed class StagedPluginChange
{
    /// <summary>变更内容。</summary>
    public required StagedChangeDetail Change { get; init; }

    /// <summary>暂存时间（UTC）。</summary>
    public required DateTimeOffset StagedAt { get; init; }

    /// <summary>插件包大小（字节）。</summary>
    public required long SizeBytes { get; init; }
}

/// <summary>暂存变更的类型，名称与宿主一致。</summary>
public enum StagedChangeKind
{
    /// <summary>安装新插件。</summary>
    Install,

    /// <summary>替换已安装的插件。</summary>
    Upgrade,

    /// <summary>删除已安装的插件。</summary>
    Remove
}

/// <summary>暂存变更的内容，与宿主的插件包变更格式一致。</summary>
public sealed class StagedChangeDetail
{
    /// <summary>变更类型。</summary>
    public required StagedChangeKind Kind { get; init; }

    /// <summary>插件 ID。</summary>
    public required string PluginId { get; init; }

    /// <summary>显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>已安装的版本。</summary>
    public string? FromVersion { get; init; }

    /// <summary>新插件包的版本。</summary>
    public string? ToVersion { get; init; }

    /// <summary>删除时是否同时清除插件数据；工具整体重写 <c>pending.json</c>，须保留此项。</summary>
    public bool PurgeData { get; init; }
}
