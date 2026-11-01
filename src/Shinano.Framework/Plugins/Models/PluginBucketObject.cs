namespace Shinano.Framework.Plugins.Models;

/// <summary>插件 bucket 中的一个对象。</summary>
public sealed class PluginBucketObject
{
    /// <summary>对象 key。</summary>
    public required string Key { get; init; }

    /// <summary>大小，单位字节。</summary>
    public required long Size { get; init; }

    /// <summary>最后修改时间（UTC）。</summary>
    public required DateTimeOffset LastModified { get; init; }
}
