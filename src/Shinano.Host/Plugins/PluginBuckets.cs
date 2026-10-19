using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Storage;

namespace Shinano.Host.Plugins;

/// <summary>按 <c>plugin.json</c> 声明的用途提供插件 bucket。</summary>
internal sealed class PluginBuckets : IPluginBuckets
{
    private readonly IReadOnlyCollection<string> _purposes;
    private readonly string                      _bucketPrefix;
    private readonly ObjectStorage               _storage;
    private readonly long                        _deploymentLimitBytes;

    /// <summary>创建入口。</summary>
    public PluginBuckets(
        IReadOnlyCollection<string> purposes,
        string                      bucketPrefix,
        ObjectStorage               storage,
        long                        deploymentLimitBytes)
    {
        _purposes             = purposes;
        _bucketPrefix         = bucketPrefix;
        _storage              = storage;
        _deploymentLimitBytes = deploymentLimitBytes;
    }

    /// <inheritdoc />
    public IPluginBucket Get(string purpose)
    {
        if (!_purposes.Contains(purpose))
            throw new ArgumentException($"用途 {purpose} 未在 plugin.json 中声明", nameof(purpose));
        return new PluginBucket(BucketNames.Plugin(_bucketPrefix, purpose), _storage, _deploymentLimitBytes);
    }
}
