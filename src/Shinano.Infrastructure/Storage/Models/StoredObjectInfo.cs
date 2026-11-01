namespace Shinano.Infrastructure.Storage.Models;

/// <summary>对象的元数据。</summary>
public sealed class StoredObjectInfo
{
    /// <summary>对象 key。</summary>
    public required string Key { get; init; }

    /// <summary>大小，单位字节。</summary>
    public required long Size { get; init; }

    /// <summary>最后修改时间（UTC）。</summary>
    public required DateTimeOffset LastModified { get; init; }
}
