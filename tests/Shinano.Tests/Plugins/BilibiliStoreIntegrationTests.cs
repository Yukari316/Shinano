using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Hosting;
using Shinano.Plugin.Bilibili;
using Shinano.Plugin.Bilibili.Persistence;
using Shinano.Tests.Support;
using SqlSugar;

namespace Shinano.Tests.Plugins;

/// <summary>
/// B站关注列表按 B站账号区分、换号沿用推送设置与取关重置的规则，以真实数据库验证。外部服务未配置时跳过；schema 由 <see cref="InfrastructureFixture" /> 删除。
/// </summary>
[Collection(InfrastructureCollection.NAME)]
public sealed class BilibiliStoreIntegrationTests
{
    private const long ACCOUNT_A = 900001;
    private const long ACCOUNT_B = 900002;
    private const long UP_SHARED = 800001;
    private const long UP_ONLY_A = 800002;
    private const long GROUP     = 700001;

    private readonly InfrastructureFixture _fixture;

    public BilibiliStoreIntegrationTests(InfrastructureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Follows_AreScopedPerAccount_SettingsCarryOverOnSwitchAndResetOnUnfollow()
    {
        TestEnvironment.RequireConfigured();
        HostInfrastructure infrastructure = _fixture.Ready;
        BiliStore store = new(
            new TestContext
            {
                Database = new Database
                {
                    Global  = infrastructure.Database.Global,
                    Account = infrastructure.Database.Account(InfrastructureFixture.SELF_ID)
                }
            });
        store.Initialize();
        DateTimeOffset now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

        await store.ReplaceFollowsAsync(ACCOUNT_A, [Following(UP_SHARED), Following(UP_ONLY_A)], now.AddDays(-7));
        await store.SaveSubscriptionAsync(UP_SHARED, pushDynamic: true, pushLive: false, [GROUP]);
        await store.SaveSubscriptionAsync(UP_ONLY_A, pushDynamic: true, pushLive: true, [GROUP]);
        await store.SavePushedAsync(UP_SHARED, GROUP, 1_000, "1");

        // 换号：B 的关注列表不影响 A 的记录；B 也关注的 UP 沿用设置，推送进度推进到刷新时刻，不补推旧动态。
        await store.ReplaceFollowsAsync(ACCOUNT_B, [Following(UP_SHARED)], now);
        Assert.Equal([UP_SHARED], (await store.FollowsAsync(ACCOUNT_B)).Select(follow => follow.Uid));
        Assert.Equal([UP_SHARED, UP_ONLY_A], (await store.FollowsAsync(ACCOUNT_A)).Select(follow => follow.Uid));
        Assert.Contains(await store.SubscriptionsAsync(), subscription => subscription.Uid == UP_SHARED && subscription.PushDynamic);
        BiliTargetRecord shared = Assert.Single(await store.TargetsAsync(), item => item.Uid == UP_SHARED);
        Assert.Equal(now.ToUnixTimeSeconds(), shared.PushedAt);
        Assert.Null(shared.PushedId);

        // A 取关：该 UP 的推送设置与推送的群重置为初始状态，之后其他账号登录也不会推送它。
        IReadOnlyList<long> removed = await store.ReplaceFollowsAsync(ACCOUNT_A, [Following(UP_SHARED)], now);
        Assert.Equal([UP_ONLY_A], removed);
        Assert.DoesNotContain(await store.SubscriptionsAsync(), subscription => subscription.Uid == UP_ONLY_A);
        Assert.DoesNotContain(await store.TargetsAsync(), target => target.Uid == UP_ONLY_A);
        Assert.Contains(await store.SubscriptionsAsync(), subscription => subscription.Uid == UP_SHARED);
    }

    private static BiliFollowing Following(long uid) =>
        new()
        {
            Uid  = uid,
            Name = $"虚构UP{uid}",
            Face = ""
        };

    private sealed class Database : IPluginDatabase
    {
        public required ISqlSugarClient Global { get; init; }

        public required ISqlSugarClient Account { get; init; }

        public long SelfId => InfrastructureFixture.SELF_ID;

        public Task MigrateGlobalAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MigrateAccountAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    // 存储只用到数据库。
    private sealed class TestContext : IPluginInitializationContext
    {
        public required IPluginDatabase Database { get; init; }

        public string PluginId => "bilibili";

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

        public void OnEnabledChanged(Action<bool> handler) => throw new NotSupportedException();

        public IPluginNotices Notices => throw new NotSupportedException();

        public IPluginConfig<T> Config<T>() where T : class, new() => throw new NotSupportedException();

        public IPluginSecrets<T> Secrets<T>() where T : class, new() => throw new NotSupportedException();
    }
}
