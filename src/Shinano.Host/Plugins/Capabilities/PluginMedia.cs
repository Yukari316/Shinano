using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Storage;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>以插件 ID 登记引用的持久媒体访问。</summary>
internal sealed class PluginMedia : IPluginMedia
{
    private readonly string               _pluginId;
    private readonly PersistentMediaStore _store;
    private readonly MediaDownloader      _downloader;

    /// <summary>创建访问入口。</summary>
    public PluginMedia(string pluginId, PersistentMediaStore store, MediaDownloader downloader)
    {
        _pluginId   = pluginId;
        _store      = store;
        _downloader = downloader;
    }

    /// <inheritdoc />
    public async Task<long> StoreAsync(
        Stream            content,
        string            mediaType,
        string            refKey,
        long?             selfId,
        CancellationToken cancellationToken)
    {
        await using BufferedMedia media =
            await BufferedMedia.CreateAsync(content, _downloader.DefaultLimitBytes, cancellationToken);
        return await _store.StoreAsync(media, mediaType, Reference(refKey, selfId), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> StoreFromUrlAsync(
        Uri               url,
        long?             declaredSize,
        string            mediaType,
        string            refKey,
        long?             selfId,
        CancellationToken cancellationToken)
    {
        await using BufferedMedia media = await _downloader.DownloadAsync(url, declaredSize, cancellationToken);
        return await _store.StoreAsync(media, mediaType, Reference(refKey, selfId), cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> AddReferenceAsync(long mediaId, string refKey, long? selfId) =>
        _store.AddReferenceAsync(mediaId, Reference(refKey, selfId));

    /// <inheritdoc />
    public Task RemoveReferencesAsync(string refKey) => _store.RemoveReferencesAsync(_pluginId, refKey);

    /// <inheritdoc />
    public Task<string?> CreateImUrlAsync(long mediaId) => _store.CreateImUrlAsync(mediaId);

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(long mediaId, CancellationToken cancellationToken)
    {
        StoredObject? stored = await _store.OpenReadAsync(mediaId, cancellationToken);
        return stored?.Content;
    }

    private MediaReference Reference(string refKey, long? selfId) =>
        new()
        {
            PluginId = _pluginId,
            RefKey   = refKey,
            SelfId   = selfId
        };
}
