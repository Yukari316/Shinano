using Shinano.Framework.Plugins.Capabilities;
using Shinano.Infrastructure.Media.Models;
using Shinano.Infrastructure.Storage;
using Shinano.Infrastructure.Storage.Models;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>插件自有的一个 bucket，只能访问固定的 bucket 名。</summary>
internal sealed class PluginBucket : IPluginBucket
{
    private readonly ObjectStorage _storage;
    private readonly long          _deploymentLimitBytes;

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
    public Task EnsureCreatedAsync(CancellationToken cancellationToken) =>
        _storage.EnsureBucketAsync(Name, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(CancellationToken cancellationToken) => _storage.DeleteBucketAsync(Name, cancellationToken);

    /// <inheritdoc />
    public async Task PutAsync(
        string            key,
        Stream            content,
        string            mediaType,
        long?             limitBytes,
        CancellationToken cancellationToken)
    {
        long limit = Math.Min(limitBytes ?? _deploymentLimitBytes, _deploymentLimitBytes);
        await using BufferedMedia media = await BufferedMedia.CreateAsync(content, limit, cancellationToken);
        await _storage.PutFileAsync(Name, key, media.FilePath, mediaType, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        StoredObject? stored = await _storage.OpenReadAsync(Name, key, cancellationToken);
        return stored?.Content;
    }

    /// <inheritdoc />
    public Task DeleteObjectAsync(string key, CancellationToken cancellationToken) =>
        _storage.DeleteAsync(Name, key, cancellationToken);

    /// <inheritdoc />
    public Task<string> CreateImUrlAsync(string key) => _storage.CreateImUrlAsync(Name, key);
}
