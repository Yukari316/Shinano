using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Database.Models;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Media.Entities;
using Shinano.Infrastructure.Media.Models;
using Shinano.Infrastructure.Storage;
using Shinano.Infrastructure.Storage.Models;
using SqlSugar;

namespace Shinano.Tests.Infrastructure;

[Collection(InfrastructureCollection.NAME)]
public sealed class InfrastructureIntegrationTests
{
    private static readonly TimeZoneInfo Shanghai = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");

    private readonly InfrastructureFixture _fixture;

    public InfrastructureIntegrationTests(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PersistentMedia_DeduplicatesAndCollectsOnlyAfterLastReferenceAndGrace()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        PersistentMediaStore store = infrastructure.PersistentMedia;
        string pluginId = "test-" + Guid.NewGuid().ToString("N")[..8];
        byte[] content = Encoding.UTF8.GetBytes("persistent " + Guid.NewGuid());

        long first;
        long second;
        await using (BufferedMedia media = await Buffer(content))
            first = await store.StoreAsync(media, "text/plain", Reference(pluginId, "a"), Token);
        await using (BufferedMedia media = await Buffer(content))
            second = await store.StoreAsync(media, "text/plain", Reference(pluginId, "b"), Token);

        Assert.Equal(first, second);
        Assert.Equal(1, await CountObjectsAsync(infrastructure, BucketNames.Media(_fixture.BucketPrefix), first));

        // 回收范围是整个测试 schema，会包含其他测试留下的无引用媒体，因此只核对本测试的媒体，不断言回收数量。
        await store.RemoveReferencesAsync(pluginId, "a");
        await store.CollectOrphansAsync(DateTimeOffset.UtcNow.AddDays(2), Token);
        await using (StoredObject? kept = await store.OpenReadAsync(first, Token))
            Assert.NotNull(kept);

        await store.RemoveReferencesAsync(pluginId, "b");
        await store.CollectOrphansAsync(DateTimeOffset.UtcNow, Token);
        await using (StoredObject? kept = await store.OpenReadAsync(first, Token))
            Assert.NotNull(kept);
        Assert.True(await store.CollectOrphansAsync(DateTimeOffset.UtcNow.AddHours(25), Token) >= 1);

        Assert.Null(await store.OpenReadAsync(first, Token));
        Assert.False(await store.AddReferenceAsync(first, Reference(pluginId, "c")));
    }

    [Fact]
    public async Task PersistentMedia_ReReferenceDuringGrace_KeepsMedia()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        PersistentMediaStore store = infrastructure.PersistentMedia;
        string pluginId = "test-" + Guid.NewGuid().ToString("N")[..8];
        long id;
        await using (BufferedMedia media = await Buffer(Encoding.UTF8.GetBytes("grace " + Guid.NewGuid())))
            id = await store.StoreAsync(media, "text/plain", Reference(pluginId, "a"), Token);
        await store.RemoveReferencesAsync(pluginId, "a");

        Assert.True(await store.AddReferenceAsync(id, Reference(pluginId, "b")));
        await store.CollectOrphansAsync(DateTimeOffset.UtcNow.AddDays(2), Token);

        await using StoredObject? stored = await store.OpenReadAsync(id, Token);
        Assert.NotNull(stored);
        Assert.NotNull(await store.CreateImUrlAsync(id));
        await store.RemovePluginReferencesAsync(pluginId);
    }

    [Fact]
    public async Task ChatMedia_ReusesContentAndExpiresByLastReference()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        ChatMediaStore store = infrastructure.ChatMedia;
        byte[] content = Encoding.UTF8.GetBytes("chat " + Guid.NewGuid());

        long first;
        long second;
        await using (BufferedMedia media = await Buffer(content))
            first = await store.StoreAsync(media, "text/plain", Token);
        await using (BufferedMedia media = await Buffer(content))
            second = await store.StoreAsync(media, "text/plain", Token);
        Assert.Equal(first, second);

        await store.ExpireAsync(DateTimeOffset.UtcNow.AddDays(-1), Token);
        await using (StoredObject? kept = await store.OpenReadAsync(first, Token))
            Assert.NotNull(kept);

        await store.ExpireAsync(DateTimeOffset.UtcNow.AddMinutes(1), Token);
        Assert.Null(await store.OpenReadAsync(first, Token));
    }

    [Fact]
    public async Task BufferedMedia_OverLimit_IsRejected()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        using MemoryStream source = new(new byte[infrastructure.Downloader.DefaultLimitBytes + 1]);

        await Assert.ThrowsAsync<MediaTooLargeException>(() =>
            BufferedMedia.CreateAsync(source, infrastructure.Downloader.DefaultLimitBytes, Token));
    }

    [Fact]
    public async Task Partitions_CoverDaysContiguously_AcrossTimeZoneChange_AndDropExpired()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        long selfId = InfrastructureFixture.SELF_ID + 1;
        await infrastructure.Database.EnsureSchemaAsync(infrastructure.Database.Schemas.Account(selfId), Token);
        await ChatMessageTable.EnsureAsync(infrastructure.Database, selfId, Token);
        ChatPartitionMaintainer maintainer = new(infrastructure.Database, NullLogger<ChatPartitionMaintainer>.Instance);
        DateTimeOffset now = new(2026, 10, 18, 4, 0, 0, TimeSpan.Zero);

        try
        {
            Assert.Equal(5, await maintainer.EnsurePartitionsAsync(selfId, now, Shanghai, 3, Token));
            Assert.Equal(0, await maintainer.EnsurePartitionsAsync(selfId, now, Shanghai, 3, Token));

            // 改为 UTC 后，新分区从已有上界接续。
            await maintainer.EnsurePartitionsAsync(selfId, now.AddDays(2), TimeZoneInfo.Utc, 3, Token);
            List<ChatPartition> partitions = [.. (await maintainer.ListAsync(selfId, Token)).OrderBy(item => item.From)];
            for (int i = 1; i < partitions.Count; i++)
                Assert.Equal(partitions[i - 1].To, partitions[i].From);
            Assert.Equal(ChatPartitionMaintainer.StartOfDay(new DateOnly(2026, 10, 17), Shanghai), partitions[0].From);

            await InsertMessageAsync(infrastructure, selfId, now);
            int dropped = await maintainer.DropExpiredAsync(selfId, now.AddDays(40), Shanghai, 30, Token);
            Assert.True(dropped >= 1);
            Assert.All(
                await maintainer.ListAsync(selfId, Token),
                item => Assert.True(item.To > ChatPartitionMaintainer.StartOfDay(new DateOnly(2026, 10, 27), Shanghai)));
        }
        finally
        {
            await infrastructure.Database.Global.Ado.ExecuteCommandAsync(
                $"drop schema if exists {infrastructure.Database.Schemas.Account(selfId)} cascade");
        }
    }

    [Fact]
    public async Task SchemaMigrator_AppliesOnce_AndRollsBackFailedMigration()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        SqlSugarScope client = infrastructure.Database.Account(InfrastructureFixture.SELF_ID);
        SchemaMigrator migrator = new(NullLogger<SchemaMigrator>.Instance);
        string owner = "test-" + Guid.NewGuid().ToString("N")[..8];
        string table = owner.Replace('-', '_');
        int runs = 0;
        SchemaMigration create = new()
        {
            Version = 1,
            Apply = async (db, _) =>
            {
                runs++;
                await db.Ado.ExecuteCommandAsync($"create table {table} (id int)");
            }
        };

        Assert.Equal(1, await migrator.ApplyAsync(client, owner, [create], Token));
        Assert.Equal(1, await migrator.ApplyAsync(client, owner, [create], Token));
        Assert.Equal(1, runs);

        SchemaMigration broken = new()
        {
            Version = 2,
            Apply = async (db, _) =>
            {
                await db.Ado.ExecuteCommandAsync($"alter table {table} add column name text");
                await db.Ado.ExecuteCommandAsync("select * from table_that_does_not_exist");
            }
        };
        await Assert.ThrowsAnyAsync<Exception>(() => migrator.ApplyAsync(client, owner, [create, broken], Token));
        int columns = await client.Ado.GetIntAsync(
            "select count(*) from information_schema.columns where table_schema = current_schema() and table_name = @table",
            new { table },
            Token);
        Assert.Equal(1, columns);
    }

    private static async Task InsertMessageAsync(HostInfrastructure infrastructure, long selfId, DateTimeOffset sentAt) =>
        await infrastructure.Database.Account(selfId).Ado.ExecuteCommandAsync(
            """
            insert into chat_message (sent_at, conversation_type, conversation_id, sender_id, message_id, text)
            values (@sentAt, 1, 1, 1, 1, 'x')
            """,
            new { sentAt });

    private static async Task<int> CountObjectsAsync(HostInfrastructure infrastructure, string bucket, long mediaId)
    {
        string? key = await infrastructure.Database.Global.Queryable<MediaObjectRecord>()
                                          .Where(item => item.Id == mediaId)
                                          .Select(item => item.ObjectKey)
                                          .FirstAsync();
        Assert.NotNull(key);
        string sha256 = key.Split('-')[0];
        using AmazonS3Client client = new(
            TestEnvironment.S3AccessKey,
            TestEnvironment.S3SecretKey,
            new AmazonS3Config
            {
                ServiceURL           = TestEnvironment.S3Endpoint,
                ForcePathStyle       = true,
                AuthenticationRegion = "us-east-1"
            });
        ListObjectsV2Response response = await client.ListObjectsV2Async(
            new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix     = sha256
            },
            Token);
        return response.S3Objects?.Count ?? 0;
    }

    private static Task<BufferedMedia> Buffer(byte[] content) =>
        BufferedMedia.CreateAsync(new MemoryStream(content), 1024 * 1024, Token);

    private static MediaReference Reference(string pluginId, string refKey) =>
        new()
        {
            PluginId = pluginId,
            RefKey   = refKey,
            SelfId   = InfrastructureFixture.SELF_ID
        };
}
