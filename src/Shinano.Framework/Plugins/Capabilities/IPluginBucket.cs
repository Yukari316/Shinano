namespace Shinano.Framework.Plugins.Capabilities;

/// <summary>
/// 插件自有的一个 bucket（<c>&lt;prefix&gt;-&lt;用途&gt;</c>），对象生命周期由插件自行管理。
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
    /// 上传对象。内容超过 <paramref name="limitBytes" /> 或部署配置的单文件上限（取较小者）时中止并抛出异常。
    /// </summary>
    Task PutAsync(string key, Stream content, string mediaType, long? limitBytes, CancellationToken cancellationToken);

    /// <summary>读取对象，调用方负责释放；对象不存在时返回 null。</summary>
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>删除对象；不存在时视为成功。</summary>
    Task DeleteObjectAsync(string key, CancellationToken cancellationToken);

    /// <summary>生成 IM 实现端可下载的临时链接，不检查对象是否存在。</summary>
    Task<string> CreateImUrlAsync(string key);
}
