using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shinano.Framework.Plugins;
using Shinano.Infrastructure.Hosting;
using Shinano.Plugin.Gemini.Api;
using Shinano.Plugin.Gemini.Persistence;
using Shinano.Tests.Host;
using Shinano.Tests.Host.Plugins;
using Shinano.Tests.Support;
using SqlSugar;

namespace Shinano.Tests.Plugins.Gemini;

/// <summary>
/// Gemini 响应白名单与模型配置在真实 PostgreSQL 全局 schema 下的建表、保存、覆盖与重载验证。
/// 外部服务未配置时跳过；测试 schema 由 <see cref="InfrastructureFixture" /> 清理。
/// </summary>
[Collection(InfrastructureCollection.NAME)]
public sealed class GeminiStoreIntegrationTests
{
    private const long GROUP_ALLOWED  = 800001;
    private const long GROUP_DENIED   = 800002;
    private const long FRIEND_ALLOWED = 900001;
    private const long FRIEND_DENIED  = 900002;

    private readonly InfrastructureFixture _fixture;

    public GeminiStoreIntegrationTests(InfrastructureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task ScopeStore_DefaultsToEmptyWhitelistAndPersistsIds()
    {
        TestEnvironment.RequireConfigured();
        HostInfrastructure infrastructure = _fixture.Ready;
        await ClearTablesAsync(infrastructure);
        StoreTestContext context = new() { Database = new GlobalDatabase { Global = infrastructure.Database.Global } };

        GeminiScopeStore store = new(context, NullLogger<GeminiScopeStore>.Instance);
        await store.LoadAsync();

        // 空名单默认不做任何响应。
        Assert.False(store.Allows(GeminiScopeKind.Group, GROUP_ALLOWED));
        Assert.False(store.Allows(GeminiScopeKind.Private, FRIEND_ALLOWED));

        await store.SaveAsync(
            new GeminiScopeRecord
            {
                Kind = GeminiScopeKind.Group,
                Ids  = [GROUP_ALLOWED]
            });
        await store.SaveAsync(
            new GeminiScopeRecord
            {
                Kind = GeminiScopeKind.Private,
                Ids  = [FRIEND_ALLOWED]
            });
        // 覆盖已有记录。
        await store.SaveAsync(
            new GeminiScopeRecord
            {
                Kind = GeminiScopeKind.Private,
                Ids  = [FRIEND_ALLOWED, FRIEND_ALLOWED + 10]
            });

        GeminiScopeStore reloaded = new(context, NullLogger<GeminiScopeStore>.Instance);
        await reloaded.LoadAsync();

        Assert.True(reloaded.Allows(GeminiScopeKind.Group, GROUP_ALLOWED));
        Assert.False(reloaded.Allows(GeminiScopeKind.Group, GROUP_DENIED));
        Assert.True(reloaded.Allows(GeminiScopeKind.Private, FRIEND_ALLOWED));
        Assert.False(reloaded.Allows(GeminiScopeKind.Private, FRIEND_DENIED));
        Assert.Equal([FRIEND_ALLOWED, FRIEND_ALLOWED + 10], reloaded.Get(GeminiScopeKind.Private).Ids);
    }

    [Fact]
    public async Task ModelSettingsStore_DefaultsToHydrogenMediumAndPersistsUpdates()
    {
        TestEnvironment.RequireConfigured();
        HostInfrastructure infrastructure = _fixture.Ready;
        await ClearTablesAsync(infrastructure);
        StoreTestContext context = new() { Database = new GlobalDatabase { Global = infrastructure.Database.Global } };

        GeminiModelSettingsStore store = new(context, NullLogger<GeminiModelSettingsStore>.Instance);
        await store.LoadAsync();

        Assert.Equal("gemini-4-hydrogen", store.Current.Model);
        Assert.Equal("medium", store.Current.Effort);

        await store.SaveAsync("gemini-4-neon", "high");
        await store.SaveAsync("gemini-3.8-flash", "low");

        GeminiModelSettingsStore reloaded = new(context, NullLogger<GeminiModelSettingsStore>.Instance);
        await reloaded.LoadAsync();

        Assert.Equal("gemini-3.8-flash", reloaded.Current.Model);
        Assert.Equal("low", reloaded.Current.Effort);
    }

    [Fact]
    public async Task UsageStore_AggregatesTokensCacheAndCostAcrossDays()
    {
        TestEnvironment.RequireConfigured();
        HostInfrastructure infrastructure = _fixture.Ready;
        await ClearTablesAsync(infrastructure);
        StoreTestContext context = new() { Database = new GlobalDatabase { Global = infrastructure.Database.Global } };
        FakeTimeProvider time = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
        GeminiUsageStore store = new(context, time, NullLogger<GeminiUsageStore>.Instance);
        await store.LoadAsync();

        GeminiUsageMetadata first = new()
        {
            PromptTokens     = 1000,
            CachedTokens     = 500,
            CandidatesTokens = 200,
            ThoughtsTokens   = 100,
            TotalTokens      = 1300
        };
        await store.RecordAsync("gemini-4-hydrogen", succeeded: true, first, GeminiPricing.EstimateCostUsd("gemini-4-hydrogen", first));
        await store.RecordAsync("gemini-4-hydrogen", succeeded: false, usage: null, costUsd: 0m);

        time.Advance(TimeSpan.FromDays(1));
        await store.RecordAsync("gemini-4-neon", succeeded: true, first, GeminiPricing.EstimateCostUsd("gemini-4-neon", first));

        IReadOnlyList<GeminiUsageRecord> lastDay = await store.QueryAsync(days: 1);
        GeminiUsageRecord neonRow = Assert.Single(lastDay);
        Assert.Equal("gemini-4-neon", neonRow.Model);
        Assert.Equal(1, neonRow.RequestCount);

        IReadOnlyList<GeminiUsageRecord> all = await store.QueryAsync(days: 0);
        Assert.Equal(2, all.Count);
        GeminiUsageRecord hydrogenRow = Assert.Single(all, row => row.Model == "gemini-4-hydrogen");
        Assert.Equal(2, hydrogenRow.RequestCount);
        Assert.Equal(1, hydrogenRow.SuccessCount);
        Assert.Equal(1000, hydrogenRow.PromptTokens);
        Assert.Equal(500, hydrogenRow.CachedTokens);
        Assert.True(hydrogenRow.CostUsd > 0m);
    }

    [Fact]
    public async Task Panel_LoadsBundledGeminiPluginAndServesConfigSecretsScopeAndModelApi()
    {
        TestEnvironment.RequireConfigured();
        await ClearTablesAsync(_fixture.Ready);
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PanelHarness panel = await PanelHarness.StartAsync(_fixture.Ready, _fixture.BucketPrefix, "artifacts/plugins", "gemini");
        using HttpClient client = panel.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/login", new { token = panel.Token }, ct)).StatusCode);

        // 停用状态下可读取通用配置与机密状态，专有 API 不可访问。
        JsonObject? configForm = await client.GetFromJsonAsync<JsonObject>("/api/plugins/gemini/config", ct);
        Assert.Equal("#", configForm?["value"]?["triggerPrefix"]?.GetValue<string>());
        Assert.Equal(2, configForm?["value"]?["groupMaxTurns"]?.GetValue<int>());
        Assert.Equal(8, configForm?["value"]?["privateMaxTurns"]?.GetValue<int>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/plugin-api/gemini/model", ct)).StatusCode);

        // 启用插件后专有页面与 API 生效。
        Assert.Null(await panel.Plugins.Runtime.EnableAsync("gemini", ct));

        HttpResponseMessage pageJs = await client.GetAsync("/plugin-web/gemini/index.js", ct);
        Assert.Equal(HttpStatusCode.OK, pageJs.StatusCode);

        JsonObject? modelBefore = await client.GetFromJsonAsync<JsonObject>("/api/plugin-api/gemini/model", ct);
        Assert.Equal("gemini-4-hydrogen", modelBefore?["model"]?.GetValue<string>());
        Assert.Equal("medium", modelBefore?["effort"]?.GetValue<string>());
        Assert.False(modelBefore?["hasApiKey"]?.GetValue<bool>());

        // 写入机密后不回显明文，但模型接口感知到已配置。
        HttpResponseMessage saveSecret = await client.PutAsJsonAsync(
            "/api/plugins/gemini/secrets",
            new JsonObject { ["ApiKey"] = "dummy-test-key" },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, saveSecret.StatusCode);
        string secretsList = await client.GetStringAsync("/api/plugins/gemini/secrets", ct);
        Assert.DoesNotContain("dummy-test-key", secretsList);
        JsonObject? modelAfterKey = await client.GetFromJsonAsync<JsonObject>("/api/plugin-api/gemini/model", ct);
        Assert.True(modelAfterKey?["hasApiKey"]?.GetValue<bool>());

        // 更新模型与白名单，验证读写持久化与即时生效。
        HttpResponseMessage saveModel = await client.PutAsJsonAsync(
            "/api/plugin-api/gemini/model",
            new JsonObject
            {
                ["model"]  = "gemini-4-carbon",
                ["effort"] = "xhigh"
            },
            ct);
        Assert.Equal(HttpStatusCode.OK, saveModel.StatusCode);
        JsonObject? savedModel = await saveModel.Content.ReadFromJsonAsync<JsonObject>(ct);
        // gemini-4-carbon 不支持 xhigh，自动校正为 medium。
        Assert.Equal("medium", savedModel?["effort"]?.GetValue<string>());

        HttpResponseMessage saveScope = await client.PutAsJsonAsync(
            "/api/plugin-api/gemini/scope/group",
            new JsonObject { ["ids"] = new JsonArray(PluginHarness.GROUP_ID, PluginHarness.GROUP_ID) },
            ct);
        Assert.Equal(HttpStatusCode.NoContent, saveScope.StatusCode);
        JsonObject? scopes = await client.GetFromJsonAsync<JsonObject>("/api/plugin-api/gemini/scope", ct);
        Assert.Equal(PluginHarness.GROUP_ID, Assert.Single(scopes?["group"]?["ids"]?.AsArray() ?? [])?.GetValue<long>());

        // 查询用量统计接口返回零值结构。
        JsonObject? usage = await client.GetFromJsonAsync<JsonObject>("/api/plugin-api/gemini/usage?days=30", ct);
        Assert.Equal(30, usage?["days"]?.GetValue<int>());
        Assert.Equal(0, usage?["totals"]?["requestCount"]?.GetValue<long>());

        // 经模拟 Milky 连接推送群消息，验证白名单放行与 #新对话 命令端到端回复。
        await panel.Plugins.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "#新对话");
        await panel.Plugins.ExpectReplyAsync("已开启新对话，当前会话上下文已清空。");

        // 停用插件后专有 API 恢复 404。
        await panel.Plugins.Runtime.DisableAsync("gemini", ct);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/plugin-api/gemini/model", ct)).StatusCode);
    }

    private static Task ClearTablesAsync(HostInfrastructure infrastructure) =>
        infrastructure.Database.Global.Ado.ExecuteCommandAsync("DROP TABLE IF EXISTS gemini_scope, gemini_model_settings, gemini_usage_daily");

    private sealed class GlobalDatabase : IPluginDatabase
    {
        public required ISqlSugarClient Global { get; init; }

        public ISqlSugarClient Account => throw new NotSupportedException();

        public long SelfId => throw new NotSupportedException();

        public Task MigrateGlobalAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task MigrateAccountAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StoreTestContext : IPluginInitializationContext
    {
        public required IPluginDatabase Database { get; init; }

        public string PluginId => "gemini";

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
