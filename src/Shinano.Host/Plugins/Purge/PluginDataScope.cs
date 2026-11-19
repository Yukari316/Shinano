namespace Shinano.Host.Plugins.Purge;

/// <summary>清除插件数据时取自其清单的范围：声明的数据表与 bucket 用途。</summary>
public sealed class PluginDataScope
{
    /// <summary>声明的数据表。</summary>
    public required PluginTables Tables { get; init; }

    /// <summary>插件独立 bucket 的用途名。</summary>
    public required string[] BucketPurposes { get; init; }
}
