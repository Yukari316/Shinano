using System.Collections.Concurrent;
using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Storage;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>按 <c>plugin.json</c> 声明的用途提供插件 bucket。</summary>
internal sealed class PluginBuckets : IPluginBuckets
{
    private readonly IReadOnlyCollection<string> _purposes;
    private readonly string                      _bucketPrefix;
    private readonly ObjectStorage               _storage;
    private readonly long                        _deploymentLimitBytes;

    private readonly ConcurrentDictionary<string, PluginBucket> _buckets = new(StringComparer.Ordinal);

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
        // 同一用途返回同一实例，"已创建"的记录才能跨调用保留。
        return _buckets.GetOrAdd(purpose, name => new PluginBucket(BucketNames.Plugin(_bucketPrefix, name), _storage, _deploymentLimitBytes));
    }
}
