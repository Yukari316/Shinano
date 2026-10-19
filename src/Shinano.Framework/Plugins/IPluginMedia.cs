namespace Shinano.Framework.Plugins;

/// <summary>
/// 持久媒体：宿主按内容去重保存，插件以业务键登记引用。插件只增删自己的引用，不能删除对象；
/// 最后一条引用删除后经宽限期由宿主回收。超过部署配置的单文件上限时抛出异常。
/// </summary>
public interface IPluginMedia
{
    /// <summary>保存媒体并以 <paramref name="refKey" /> 登记引用。</summary>
    /// <returns>媒体 ID。</returns>
    Task<long> StoreAsync(
        Stream            content,
        string            mediaType,
        string            refKey,
        long?             selfId,
        CancellationToken cancellationToken);

    /// <summary>下载 <paramref name="url" /> 并保存，以 <paramref name="refKey" /> 登记引用。</summary>
    /// <param name="url">媒体地址。</param>
    /// <param name="declaredSize">消息段声明的大小，未知时为 null，超限时不下载。</param>
    /// <param name="mediaType">MIME 类型。</param>
    /// <param name="refKey">业务键。</param>
    /// <param name="selfId">业务对象所属账号，与账号无关时为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>媒体 ID。</returns>
    Task<long> StoreFromUrlAsync(
        Uri               url,
        long?             declaredSize,
        string            mediaType,
        string            refKey,
        long?             selfId,
        CancellationToken cancellationToken);

    /// <summary>为已有媒体登记引用；媒体已被回收时返回 false。</summary>
    Task<bool> AddReferenceAsync(long mediaId, string refKey, long? selfId);

    /// <summary>删除本插件 <paramref name="refKey" /> 下的全部引用。</summary>
    Task RemoveReferencesAsync(string refKey);

    /// <summary>生成 IM 实现端可下载的临时链接；媒体不存在时返回 null。</summary>
    Task<string?> CreateImUrlAsync(long mediaId);

    /// <summary>读取媒体内容，调用方负责释放；媒体不存在时返回 null。</summary>
    Task<Stream?> OpenReadAsync(long mediaId, CancellationToken cancellationToken);
}
