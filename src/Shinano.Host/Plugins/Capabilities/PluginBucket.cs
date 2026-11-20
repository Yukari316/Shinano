using System.Runtime.CompilerServices;
using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Storage;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>
/// 插件自有的一个 bucket，只能访问固定的 bucket 名；插件可以完全控制其中的对象与 bucket 本身。
/// 同一插件同一用途共用一个实例（由 <see cref="PluginBuckets" /> 保证），"已创建"的记录随实例保留。
/// </summary>
internal sealed class PluginBucket : IPluginBucket
{
    private readonly ObjectStorage _storage;
    private readonly long          _deploymentLimitBytes;
    private volatile bool          _created;

    /// <summary>创建访问入口。</summary>
    public PluginBucket(string name, ObjectStorage storage, long deploymentLimitBytes)
    {
        Name                  = name;
        _storage              = storage;
        _deploymentLimitBytes = deploymentLimitBytes;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        if (_created) return;
        await _storage.EnsureBucketAsync(Name, cancellationToken);
        _created = true;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(CancellationToken cancellationToken)
    {
        _created = false;
        await _storage.DeleteBucketAsync(Name, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> PutAsync(
        string            key,
        Stream            content,
        string            mediaType,
        long?             limitBytes,
        CancellationToken cancellationToken)
    {
        long limit = Math.Min(limitBytes ?? _deploymentLimitBytes, _deploymentLimitBytes);
        await using BufferedMedia media = await BufferedMedia.CreateAsync(content, limit, cancellationToken);
        try
        {
            await _storage.PutFileAsync(Name, key, media.FilePath, mediaType, cancellationToken);
        }
        catch (Exception exception) when (ObjectStorage.IsMissingBucket(exception))
        {
            // bucket 在宿主之外被删除：重新创建后重试一次（内容已缓冲到临时文件，可以重读），仍失败则抛出。
            _created = false;
            await EnsureCreatedAsync(cancellationToken);
            await _storage.PutFileAsync(Name, key, media.FilePath, mediaType, cancellationToken);
        }

        return media.Length;
    }

    /// <inheritdoc />
    public async Task<PluginBucketObject?> GetInfoAsync(string key, CancellationToken cancellationToken) =>
        await _storage.GetInfoAsync(Name, key, cancellationToken) is { } info ? ToObject(info) : null;

    /// <inheritdoc />
    public async IAsyncEnumerable<PluginBucketObject> ListAsync(string? prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (StoredObjectInfo info in _storage.ListAsync(Name, prefix, cancellationToken))
            yield return ToObject(info);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        StoredObject? stored = await _storage.OpenReadAsync(Name, key, cancellationToken);
        return stored?.Content;
    }

    /// <inheritdoc />
    public async Task DeleteObjectAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await _storage.DeleteAsync(Name, key, cancellationToken);
        }
        catch (Exception exception) when (ObjectStorage.IsMissingBucket(exception))
        {
            // RustFS 明确答复 bucket 不存在，对象自然也不存在；连接失败等其他错误照常抛出。
        }
    }

    /// <inheritdoc />
    public Task<string> CreateImUrlAsync(string key) => _storage.CreateImUrlAsync(Name, key);

    private static PluginBucketObject ToObject(StoredObjectInfo info) =>
        new()
        {
            Key          = info.Key,
            Size         = info.Size,
            LastModified = info.LastModified
        };
}
