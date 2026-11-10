using Npgsql;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Entities;
using Shinano.Host.Plugins.Models;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Database.Entities;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Media.Entities;
using Shinano.Infrastructure.Media.Models;
using Shinano.Infrastructure.Storage;
using Shinano.Tests.Infrastructure;
using Shinano.Tests.Restart;

namespace Shinano.Tests.Plugins;

/// <summary>
/// 插件数据清除以真实数据库与 RustFS 验证：只删除声明的表与该插件的数据，未声明的同前缀表只报告。
/// 外部服务未配置时跳过；测试 schema 与 bucket 由 <see cref="InfrastructureFixture" /> 与本测试删除。
/// </summary>
[Collection(InfrastructureCollection.NAME)]
public sealed class PluginDataPurgerIntegrationTests : IDisposable
{
    private const string PLUGIN_ID     = "purge-test";
    private const string OTHER_PLUGIN  = "purge-other";
    private const string PURPOSE       = "purgetest";
    private const long   OTHER_ACCOUNT = 10002;

    private readonly InfrastructureFixture _fixture;
    private readonly PackageFixture        _packages;

    public PluginDataPurgerIntegrationTests(InfrastructureFixture fixture)
    {
        _fixture  = fixture;
        _packages = new PackageFixture(fixture.BucketPrefix);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _packages.Dispose();

    [Fact]
    public async Task Purge_DeletesDeclaredDataOnly_AndReportsUndeclaredTables()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        ShinanoDatabase database = infrastructure.Database;
        string global = database.Schemas.Global;
        string account = database.Schemas.Account(InfrastructureFixture.SELF_ID);
        string other = database.Schemas.Account(OTHER_ACCOUNT);
        string bucket = BucketNames.Plugin(_fixture.BucketPrefix, PURPOSE);
        await database.EnsureSchemaAsync(other, Token);
        try
        {
            await ExecuteAsync(
                database,
                $"create table {global}.purge_test_note (id int)",
                $"create table {global}.purge_test_extra (id int)",
                $"create table {account}.purge_test_entry (id int)",
                $"create table {other}.purge_test_entry (id int)");
            database.Global.CodeFirst.InitTables<SchemaVersionRecord, PluginConfigRecord, PluginStateRecord>();
            await database.Global.Insertable(new[] { Version(PLUGIN_ID), Version(OTHER_PLUGIN) }).ExecuteCommandAsync(Token);
            await database.Global.Insertable(new[] { Config(PLUGIN_ID), Config(OTHER_PLUGIN) }).ExecuteCommandAsync(Token);
            await database.Global.Insertable(new[] { State(PLUGIN_ID), State(OTHER_PLUGIN) }).ExecuteCommandAsync(Token);
            await using (BufferedMedia media = await BufferedMedia.CreateAsync(new MemoryStream("purge"u8.ToArray()), 1024, Token))
                await infrastructure.PersistentMedia.StoreAsync(
                    media,
                    "text/plain",
                    new MediaReference
                    {
                        PluginId = PLUGIN_ID,
                        RefKey   = "a",
                        SelfId   = InfrastructureFixture.SELF_ID
                    },
                    Token);
            await infrastructure.Storage.EnsureBucketAsync(bucket, Token);
            string file = Path.Combine(_packages.DataDirectory, "object.txt");
            await File.WriteAllTextAsync(file, "object", Token);
            await infrastructure.Storage.PutFileAsync(bucket, "a.txt", file, "text/plain", Token);
            await infrastructure.Storage.PutFileAsync(bucket, "b.txt", file, "text/plain", Token);
            string secrets = Path.Combine(_packages.DataDirectory, "secrets", PLUGIN_ID + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(secrets) ?? _packages.DataDirectory);
            await File.WriteAllTextAsync(secrets, "{}", Token);
            string profile = Path.Combine(_packages.DataDirectory, "browser", "profiles", PLUGIN_ID);
            Directory.CreateDirectory(profile);

            PluginDataPurger purger = _packages.CreatePurger(infrastructure, _fixture.Health);
            purger.Schedule(Removal(), Scope());
            await purger.RunPendingAsync(Token);

            PluginPurge purge = Assert.Single(purger.Visible());
            Assert.Equal(PluginPurgeState.Completed, purge.State);
            Assert.All(purge.Steps, step => Assert.True(step.Done, step.Error));
            Assert.Equal([$"{global}.purge_test_extra"], purge.PossibleLeftovers);
            Assert.Equal([$"{global}.purge_test_extra"], await TablesAsync(database, [global, account, other]));
            // 同一次运行的其他测试也会写入这些表，只核对本测试的两个插件。
            string[] ids = [PLUGIN_ID, OTHER_PLUGIN];
            Assert.Equal(
                [OTHER_PLUGIN],
                await database.Global.Queryable<SchemaVersionRecord>().Where(record => ids.Contains(record.Owner)).Select(record => record.Owner).ToListAsync(Token));
            Assert.Equal(
                [OTHER_PLUGIN],
                await database.Global.Queryable<PluginConfigRecord>().Where(record => ids.Contains(record.PluginId)).Select(record => record.PluginId).ToListAsync(Token));
            Assert.Equal(
                [OTHER_PLUGIN],
                await database.Global.Queryable<PluginStateRecord>().Where(record => ids.Contains(record.PluginId)).Select(record => record.PluginId).ToListAsync(Token));
            Assert.False(await database.Global.Queryable<MediaReferenceRecord>().AnyAsync(reference => reference.PluginId == PLUGIN_ID, Token));
            Assert.Null(await infrastructure.Storage.DeleteBucketAsync(bucket, Token));
            Assert.False(File.Exists(secrets));
            Assert.False(Directory.Exists(profile));

            Assert.True(purger.Dismiss(PLUGIN_ID));
            Assert.Empty(purger.Visible());
        }
        finally
        {
            await ExecuteAsync(database, $"drop schema if exists {other} cascade", $"drop table if exists {global}.purge_test_extra");
            await infrastructure.Storage.DeleteBucketAsync(bucket, CancellationToken.None);
        }
    }

    // 失败的步骤留到下次启动重试，在那之前同 ID 的插件被重新安装时不能删除新插件的数据。
    [Fact]
    public async Task Purge_AfterPluginReinstalled_IsCancelled()
    {
        HostInfrastructure infrastructure = _fixture.Ready;
        ShinanoDatabase database = infrastructure.Database;
        string global = database.Schemas.Global;
        await ExecuteAsync(database, $"create table {global}.purge_test_note (id int)");
        try
        {
            PluginDataPurger purger = _packages.CreatePurger(infrastructure, _fixture.Health);
            purger.Schedule(Removal(), Scope());
            _packages.Install("PurgeTest", PLUGIN_ID, "2.0.0");
            await purger.RunPendingAsync(Token);

            Assert.Equal(PluginPurgeState.Cancelled, Assert.Single(purger.Visible()).State);
            Assert.Equal([$"{global}.purge_test_note"], await TablesAsync(database, [global]));
        }
        finally
        {
            await ExecuteAsync(database, $"drop table if exists {global}.purge_test_note");
        }
    }

    private static PluginChange Removal() =>
        new()
        {
            Kind        = PluginChangeKind.Remove,
            PluginId    = PLUGIN_ID,
            Name        = "清除测试",
            FromVersion = "1.0.0",
            PurgeData   = true
        };

    private static PluginDataScope Scope() =>
        new()
        {
            Tables = new PluginTables
            {
                Global  = ["purge_test_note"],
                Account = ["purge_test_entry"]
            },
            BucketPurposes = [PURPOSE]
        };

    private static SchemaVersionRecord Version(string owner) =>
        new()
        {
            Owner     = owner,
            Version   = 1,
            AppliedAt = DateTimeOffset.UtcNow
        };

    private static PluginConfigRecord Config(string pluginId) =>
        new()
        {
            PluginId  = pluginId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static PluginStateRecord State(string pluginId) =>
        new()
        {
            PluginId  = pluginId,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    private static async Task<List<string>> TablesAsync(ShinanoDatabase database, string[] schemas)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(Token);
        await using NpgsqlCommand query = new(
            "select table_schema || '.' || table_name from information_schema.tables where table_schema = any(@schemas) and table_name like 'purge%' order by 1",
            connection);
        query.Parameters.AddWithValue("schemas", schemas);
        List<string> tables = [];
        await using NpgsqlDataReader reader = await query.ExecuteReaderAsync(Token);
        while (await reader.ReadAsync(Token)) tables.Add(reader.GetString(0));
        return tables;
    }

    private static async Task ExecuteAsync(ShinanoDatabase database, params string[] statements)
    {
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(CancellationToken.None);
        foreach (string statement in statements)
        {
            await using NpgsqlCommand command = new(statement, connection);
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }
}
