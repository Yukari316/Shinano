namespace Shinano.Framework.Plugins;

/// <summary>
/// 插件自有的一个 bucket（<c>&lt;prefix&gt;-&lt;用途&gt;</c>），插件完全控制其中的对象与 bucket 本身，生命周期由插件自行管理。
/// </summary>
public interface IPluginBucket
{
    /// <summary>bucket 名。</summary>
    string Name { get; }

    /// <summary>bucket 不存在时创建。</summary>
    Task EnsureCreatedAsync(CancellationToken cancellationToken);

    /// <summary>清空并删除 bucket。</summary>
    Task DeleteAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 上传对象，同名对象被覆盖。内容超过 <paramref name="limitBytes" /> 或部署配置的单文件上限（取较小者）时中止并抛出异常。
    /// </summary>
    /// <returns>写入的字节数。</returns>
    Task<long> PutAsync(string key, Stream content, string mediaType, long? limitBytes, CancellationToken cancellationToken);

    /// <summary>读取对象，调用方负责释放；对象不存在时返回 null。</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>对象的大小与修改时间；对象不存在时返回 null。</summary>
    Task<PluginBucketObject?> GetInfoAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// 按 key 顺序列出对象，内部分页读取；<paramref name="prefix" /> 为 null 时列出全部。bucket 不存在时为空。
    /// 列出期间写入或删除的对象可能出现也可能不出现。
    /// </summary>
    IAsyncEnumerable<PluginBucketObject> ListAsync(string? prefix, CancellationToken cancellationToken);

    /// <summary>删除对象；不存在时视为成功。</summary>
    Task DeleteObjectAsync(string key, CancellationToken cancellationToken);

    /// <summary>生成 IM 实现端可下载的临时链接，不检查对象是否存在。</summary>
    Task<string> CreateImUrlAsync(string key);
}

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
