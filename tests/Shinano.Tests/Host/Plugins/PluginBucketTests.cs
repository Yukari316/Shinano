using System.Text;
using Shinano.Framework.Plugins;
using Shinano.Host.Plugins.Capabilities;
using Shinano.Infrastructure.Storage;
using Shinano.Tests.Support;

namespace Shinano.Tests.Host.Plugins;

/// <summary>插件独立 bucket 的完整控制：写入返回字节数、按前缀列出、元数据、删除对象与 bucket（使用本次运行的测试 bucket）。</summary>
[Collection(InfrastructureCollection.NAME)]
public sealed class PluginBucketTests
{
    private const long LIMIT = 1024;

    private readonly InfrastructureFixture _fixture;

    public PluginBucketTests(InfrastructureFixture fixture) => _fixture = fixture;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Bucket_ListsInfoAndDeletesObjects()
    {
        PluginBucket bucket = new($"{_fixture.BucketPrefix}-plugin", _fixture.Ready.Storage, LIMIT);
        try
        {
            Assert.Empty(await ListAsync(bucket, null));
            await bucket.EnsureCreatedAsync(Token);
            Assert.Equal(5, await PutAsync(bucket, "a/1.txt", "hello"));
            Assert.Equal(3, await PutAsync(bucket, "a/2.txt", "abc"));
            await PutAsync(bucket, "b/1.txt", "x");
            await Assert.ThrowsAnyAsync<Exception>(() => PutAsync(bucket, "too-large.txt", new string('x', (int)LIMIT + 1)));

            IReadOnlyList<PluginBucketObject> listed = await ListAsync(bucket, "a/");
            Assert.Equal(["a/1.txt", "a/2.txt"], listed.Select(item => item.Key));
            Assert.Equal([5L, 3L], listed.Select(item => item.Size));
            Assert.InRange(listed[0].LastModified, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(5));

            PluginBucketObject? info = await bucket.GetInfoAsync("a/2.txt", Token);
            Assert.Equal(3, info?.Size);
            Assert.Null(await bucket.GetInfoAsync("missing.txt", Token));

            await bucket.DeleteObjectAsync("a/1.txt", Token);
            Assert.Equal(["a/2.txt", "b/1.txt"], (await ListAsync(bucket, null)).Select(item => item.Key));
        }
        finally
        {
            await bucket.DeleteAsync(CancellationToken.None);
        }

        Assert.Empty(await ListAsync(bucket, null));
    }

    // "已创建"按实例记住；bucket 在宿主之外被删除后，写入时重新创建；bucket 不存在时删除对象视为成功，RustFS 不可达时抛出。
    [Fact]
    public async Task Bucket_RecreatesAfterExternalDeletion_AndOnlyTreatsAnsweredMissingAsDeleted()
    {
        PluginBuckets buckets = new(["recreate"], _fixture.BucketPrefix, _fixture.Ready.Storage, LIMIT);
        IPluginBucket bucket = buckets.Get("recreate");
        Assert.Same(bucket, buckets.Get("recreate"));
        try
        {
            await bucket.EnsureCreatedAsync(Token);
            await _fixture.Ready.Storage.DeleteBucketAsync(bucket.Name, Token);
            await bucket.EnsureCreatedAsync(Token);

            Assert.Equal(5, await PutAsync((PluginBucket)bucket, "a.txt", "hello"));
            Assert.Equal(["a.txt"], (await ListAsync((PluginBucket)bucket, null)).Select(item => item.Key));

            await _fixture.Ready.Storage.DeleteBucketAsync(bucket.Name, Token);
            await bucket.DeleteObjectAsync("a.txt", Token);
        }
        finally
        {
            await bucket.DeleteAsync(CancellationToken.None);
        }

        using ObjectStorage offline = new(
            new RustFsOptions
            {
                Endpoint  = "http://127.0.0.1:1",
                AccessKey = "offline",
                SecretKey = "offline"
            });
        PluginBucket unreachable = new(bucket.Name, offline, LIMIT);
        await Assert.ThrowsAnyAsync<Exception>(() => unreachable.DeleteObjectAsync("a.txt", Token));
    }

    private static async Task<long> PutAsync(PluginBucket bucket, string key, string text)
    {
        using MemoryStream content = new(Encoding.UTF8.GetBytes(text));
        return await bucket.PutAsync(key, content, "text/plain", null, Token);
    }

    private static async Task<IReadOnlyList<PluginBucketObject>> ListAsync(PluginBucket bucket, string? prefix)
    {
        List<PluginBucketObject> items = [];
        await foreach (PluginBucketObject item in bucket.ListAsync(prefix, Token))
            items.Add(item);
        return items;
    }
}
