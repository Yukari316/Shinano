namespace Shinano.Tool.Plugins.Models;

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
