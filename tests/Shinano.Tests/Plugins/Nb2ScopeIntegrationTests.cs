using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Hosting;
using Shinano.Plugin.NB2Bridge.Scope;
using Shinano.Tests.Support;
using SqlSugar;

namespace Shinano.Tests.Plugins;

/// <summary>
/// NB2 响应范围：没有记录时全部交付，黑白名单的判定，以及名单（<c>bigint[]</c> 列）保存后重新读入不变，以真实数据库验证。
/// 外部服务未配置时跳过；schema 由 <see cref="InfrastructureFixture" /> 删除。
/// </summary>
[Collection(InfrastructureCollection.NAME)]
public sealed class Nb2ScopeIntegrationTests
{
    private const long GROUP_LISTED   = 700001;
    private const long GROUP_UNLISTED = 700002;
    private const long FRIEND_LISTED  = 600001;
    private const long FRIEND_OTHER   = 600002;

    private readonly InfrastructureFixture _fixture;

    public Nb2ScopeIntegrationTests(InfrastructureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Scope_DefaultsToAllAndPersistsWhitelistAndBlacklist()
    {
        TestEnvironment.RequireConfigured();
        HostInfrastructure infrastructure = _fixture.Ready;
        TestContext context = new() { Database = new GlobalDatabase { Global = infrastructure.Database.Global } };
        Nb2ScopeStore store = new(context, NullLogger<Nb2ScopeStore>.Instance);
        await store.LoadAsync();

        Assert.True(store.Allows(Nb2ScopeKind.Group, GROUP_UNLISTED));
        Assert.True(store.Allows(Nb2ScopeKind.Private, FRIEND_OTHER));

        await store.SaveAsync(Record(Nb2ScopeKind.Group, whitelist: true, [GROUP_LISTED]));
        await store.SaveAsync(Record(Nb2ScopeKind.Private, whitelist: false, [FRIEND_LISTED]));
        // 覆盖已有记录。
        await store.SaveAsync(Record(Nb2ScopeKind.Private, whitelist: false, [FRIEND_LISTED, FRIEND_OTHER + 1]));

        Nb2ScopeStore reloaded = new(context, NullLogger<Nb2ScopeStore>.Instance);
        await reloaded.LoadAsync();
        Assert.True(reloaded.Allows(Nb2ScopeKind.Group, GROUP_LISTED));
        Assert.False(reloaded.Allows(Nb2ScopeKind.Group, GROUP_UNLISTED));
        Assert.False(reloaded.Allows(Nb2ScopeKind.Private, FRIEND_LISTED));
        Assert.True(reloaded.Allows(Nb2ScopeKind.Private, FRIEND_OTHER));
        Assert.Equal([FRIEND_LISTED, FRIEND_OTHER + 1], reloaded.Get(Nb2ScopeKind.Private).Ids);
    }

    private static Nb2ScopeRecord Record(Nb2ScopeKind kind, bool whitelist, long[] ids) =>
        new()
        {
            Kind      = kind,
            Whitelist = whitelist,
            Ids       = ids
        };

    private sealed class GlobalDatabase : IPluginDatabase
    {
        public required ISqlSugarClient Global { get; init; }

        public ISqlSugarClient Account => throw new NotSupportedException();

        public long SelfId => throw new NotSupportedException();

        public Task MigrateGlobalAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MigrateAccountAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // 存储只用到数据库。
    private sealed class TestContext : IPluginInitializationContext
    {
        public required IPluginDatabase Database { get; init; }

        public string PluginId => "nb2-bridge";

        public IPluginEvents Events => throw new NotSupportedException();

        public IPluginScheduler Scheduler => throw new NotSupportedException();

        public IPluginMedia Media => throw new NotSupportedException();

        public IPluginBuckets Buckets => throw new NotSupportedException();

        public IBotAccessor Bot => throw new NotSupportedException();

        public IPluginAssets Assets => throw new NotSupportedException();

        public IPluginPanel Panel => throw new NotSupportedException();

        public IPluginFonts Fonts => throw new NotSupportedException();

        public IPluginImages Images => throw new NotSupportedException();

        public IPluginBrowser Browser => throw new NotSupportedException();

        public IPluginTools Tools => throw new NotSupportedException();

        public string DataDirectory => throw new NotSupportedException();

        public TimeZoneInfo TimeZone => TimeZoneInfo.Utc;

        public void OnEnabledChanged(Action<bool> handler) => throw new NotSupportedException();

        public IPluginNotices Notices => throw new NotSupportedException();

        public IPluginConfig<T> Config<T>() where T : class, new() => throw new NotSupportedException();

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }
}
