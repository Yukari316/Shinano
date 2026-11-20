namespace Shinano.Framework.Plugins;

/// <summary><see cref="IPluginBucket" /> 的常用操作。</summary>
public static class PluginBucketExtensions
{
    /// <summary>
    /// 删除 key 以 <paramref name="prefix" /> 开头（为 null 时为全部对象）、最后修改时间早于 <paramref name="cutoff" /> 的对象，
    /// 用于清理插件发送后暂存的临时对象。逐个删除，某个对象删除失败时抛出异常，已删除的不恢复。
    /// </summary>
    /// <returns>删除的对象数。</returns>
    public static async Task<int> DeleteOlderThanAsync(
        this IPluginBucket bucket,
        string?            prefix,
        DateTimeOffset     cutoff,
        CancellationToken  cancellationToken)
    {
        int deleted = 0;
        await foreach (PluginBucketObject item in bucket.ListAsync(prefix, cancellationToken))
        {
            if (item.LastModified >= cutoff) continue;
            await bucket.DeleteObjectAsync(item.Key, cancellationToken);
            deleted++;
        }

        return deleted;
    }
}
