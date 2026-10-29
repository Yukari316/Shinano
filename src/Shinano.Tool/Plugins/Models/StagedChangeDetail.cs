namespace Shinano.Tool.Plugins.Models;

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
}
