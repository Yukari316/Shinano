using Shinano.Host.Panel.Auth;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.ChatArchive.Persistence;
using Shinano.Tests.Host.Plugins;
using Shinano.Tests.Support;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Shinano.Tests.Host;

[Collection(InfrastructureCollection.NAME)]
public sealed class PanelApiTests
{
    private const string TEST_PLUGINS = "artifacts/test-plugins";
    private const string PLUGIN_A     = "sample-a";
    private const string PLUGIN_B     = "sample-b";

    private readonly InfrastructureFixture _fixture;

    public PanelApiTests(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_RequiresToken_ThrottlesFailures_AndRegenerationEndsSessions()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient anonymous = panel.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/overview", Token)).StatusCode);
        Assert.Equal(PanelTokenStore.TOKEN_LENGTH, panel.Token.Length);

        using HttpClient client = panel.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(client, "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(client, panel.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/overview", Token)).StatusCode);

        HttpResponseMessage regenerated = await client.PostAsync("/api/auth/token", null, Token);
        string? newToken = (await regenerated.Content.ReadFromJsonAsync<JsonObject>(Token))?["token"]?.GetValue<string>();
        Assert.Equal(panel.Token, newToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/overview", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(client, newToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/overview", Token)).StatusCode);

        // 失败次数达到上限后，即使 Token 正确也拒绝，直到窗口结束。
        using HttpClient attacker = panel.CreateClient();
        for (int i = 0; i < LoginThrottle.MAX_FAILURES; i++)
            await LoginAsync(attacker, "wrong");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginAsync(attacker, newToken)).StatusCode);
    }

    [Fact]
    public async Task ConfigForm_ExportsAnnotatedSchema_ValidatesAndAppliesToPlugin()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);
        Assert.Null(await panel.Plugins.Runtime.EnableAsync(PLUGIN_A, Token));

        JsonObject? form = await client.GetFromJsonAsync<JsonObject>($"/api/plugins/{PLUGIN_A}/config", Token);
        JsonNode? properties = form?["schema"]?["properties"];
        Assert.Equal("问候语", properties?["greeting"]?["title"]?.GetValue<string>());
        Assert.Equal("config-a 指令回复的内容", properties?["greeting"]?["description"]?.GetValue<string>());
        Assert.Equal(20, properties?["greeting"]?["maxLength"]?.GetValue<int>());
        Assert.Equal(1, properties?["times"]?["x-order"]?.GetValue<int>());
        Assert.Equal(10, properties?["times"]?["maximum"]?.GetValue<double>());
        Assert.Equal("integer", properties?["times"]?["type"]?.GetValue<string>());
        Assert.Contains("Loud", properties?["mode"]?["enum"]?.AsArray().Select(item => item?.GetValue<string>()) ?? []);
        Assert.Contains("greeting", form?["schema"]?["required"]?.AsArray().Select(item => item?.GetValue<string>()) ?? []);
        Assert.Equal("hi", form?["value"]?["greeting"]?.GetValue<string>());

        HttpResponseMessage invalid = await client.PutAsJsonAsync(
            $"/api/plugins/{PLUGIN_A}/config",
            new JsonObject
            {
                ["greeting"] = "hello",
                ["times"]    = 11,
                ["mode"]     = "Loud"
            },
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        HttpResponseMessage saved = await client.PutAsJsonAsync(
            $"/api/plugins/{PLUGIN_A}/config",
            new JsonObject
            {
                ["greeting"] = "hello",
                ["times"]    = 3,
                ["mode"]     = "Loud"
            },
            Token);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);

        await panel.Plugins.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "config-a");
        await panel.Plugins.ExpectReplyAsync("hello");
    }

    [Fact]
    public async Task PluginSecrets_AreWriteOnly_AndAppliedImmediately()
    {
        const string SECRET = "s3cret-value";
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);
        Assert.Null(await panel.Plugins.Runtime.EnableAsync(PLUGIN_A, Token));

        JsonArray? fields = await client.GetFromJsonAsync<JsonArray>($"/api/plugins/{PLUGIN_A}/secrets", Token);
        Assert.Equal(["ApiKey", "Session"], fields?.Select(field => field?["key"]?.GetValue<string>()));
        Assert.Equal("密钥", fields?[0]?["label"]?.GetValue<string>());
        Assert.False(fields?[0]?["configured"]?.GetValue<bool>());
        Assert.False(fields?[1]?["editable"]?.GetValue<bool>());

        HttpResponseMessage invalid = await client.PutAsJsonAsync(
            $"/api/plugins/{PLUGIN_A}/secrets",
            new JsonObject { ["Session"] = "x", ["ApiKey"] = "" },
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(2, (await invalid.Content.ReadFromJsonAsync<JsonObject>(Token))?["errors"]?.AsArray().Count);

        HttpResponseMessage saved = await client.PutAsJsonAsync($"/api/plugins/{PLUGIN_A}/secrets", new JsonObject { ["ApiKey"] = SECRET }, Token);
        Assert.Equal(HttpStatusCode.NoContent, saved.StatusCode);
        string listed = await client.GetStringAsync($"/api/plugins/{PLUGIN_A}/secrets", Token);
        Assert.DoesNotContain(SECRET, listed);
        Assert.True(JsonNode.Parse(listed)?[0]?["configured"]?.GetValue<bool>());
        await panel.Plugins.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "secret-a");
        await panel.Plugins.ExpectReplyAsync($"secret-a={SECRET.Length}");

        HttpResponseMessage cleared = await client.PutAsJsonAsync($"/api/plugins/{PLUGIN_A}/secrets", new JsonObject { ["ApiKey"] = null }, Token);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        await panel.Plugins.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "secret-a");
        await panel.Plugins.ExpectReplyAsync("secret-a=0");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/plugins/{PLUGIN_B}/secrets", Token)).StatusCode);
    }

    [Fact]
    public async Task PluginPanel_IsAvailableOnlyWhileEnabled()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);
        using HttpClient anonymous = panel.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/plugin-web/{PLUGIN_B}/index.js", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/plugins/{PLUGIN_B}/config", Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/plugins/{PLUGIN_B}/enable", null, Token)).StatusCode);
        JsonArray? plugins = await client.GetFromJsonAsync<JsonArray>("/api/plugins", Token);
        JsonNode? sampleB = plugins?.Single(plugin => plugin?["id"]?.GetValue<string>() == PLUGIN_B);
        Assert.Equal("样例页面", sampleB?["pageTitle"]?.GetValue<string>());
        Assert.Equal("index.js", sampleB?["pageEntry"]?.GetValue<string>());

        HttpResponseMessage script = await client.GetAsync($"/plugin-web/{PLUGIN_B}/index.js", Token);
        Assert.Equal(HttpStatusCode.OK, script.StatusCode);
        Assert.Equal("text/javascript", script.Content.Headers.ContentType?.MediaType);
        Assert.NotEqual(HttpStatusCode.OK, (await client.GetAsync($"/plugin-web/{PLUGIN_B}/%2E%2E%2Fplugin.json", Token)).StatusCode);
        Assert.Equal("pong-b", await client.GetStringAsync($"/api/plugin-api/{PLUGIN_B}/ping", Token));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/plugin-api/{PLUGIN_B}/ping", Token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/plugins/{PLUGIN_B}/disable", null, Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/plugin-api/{PLUGIN_B}/ping", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/plugin-web/{PLUGIN_B}/index.js", Token)).StatusCode);
    }

    [Fact]
    public async Task HostConfig_SecretsAreWriteOnly_ValidatedAndWrittenToLocalConfig()
    {
        const string PASSWORD = "pfx-password";
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);

        JsonArray? fields = await client.GetFromJsonAsync<JsonArray>("/api/config", Token);
        Assert.DoesNotContain(fields ?? [], item => item?["field"]?["key"]?.GetValue<string>() == "Token");
        Assert.DoesNotContain(panel.Token, fields?.ToJsonString() ?? string.Empty);

        HttpResponseMessage invalid = await client.PutAsJsonAsync(
            "/api/config",
            new JsonObject
            {
                ["LlBot"]    = new JsonObject { ["Port"] = 70000 },
                ["Web"]      = new JsonObject { ["Token"] = "x", ["ListenAddress"] = "0.0.0.0" },
                ["Monitor"]  = new JsonObject { ["ListenAddress"] = "not-an-ip" },
                ["Database"] = new JsonObject { ["Dsn"] = null }
            },
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        // 启动时会被拒绝的监听地址（全部网卡、无法解析）在保存时就拒绝，避免重启后管理员被锁在外面。
        Assert.Equal(5, (await invalid.Content.ReadFromJsonAsync<JsonObject>(Token))?["errors"]?.AsArray().Count);

        // 修改数据库 DSN 时先实际连接，连不上则不写入。
        HttpResponseMessage unreachable = await client.PutAsJsonAsync(
            "/api/config",
            new JsonObject { ["Database"] = new JsonObject { ["Dsn"] = "postgres://user:pass@127.0.0.1:1/none" } },
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, unreachable.StatusCode);
        Assert.DoesNotContain("pass@", await unreachable.Content.ReadAsStringAsync(Token));

        // 证书路径为空时证书密码不需要检查，可直接保存；保存后只报告已配置。
        HttpResponseMessage saved = await client.PutAsJsonAsync(
            "/api/config",
            new JsonObject
            {
                ["Logging"]    = new JsonObject { ["MinimumLevel"] = "Warning" },
                ["Deployment"] = new JsonObject { ["SuperUsers"] = new JsonArray(1, 2) },
                ["Monitor"]    = new JsonObject { ["CertificatePassword"] = PASSWORD }
            },
            Token);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        string listed = await client.GetStringAsync("/api/config", Token);
        Assert.DoesNotContain(PASSWORD, listed);

        JsonNode? local = JsonNode.Parse(await File.ReadAllTextAsync(panel.LocalConfigPath, Token));
        Assert.Equal("Warning", local?["Shinano"]?["Logging"]?["MinimumLevel"]?.GetValue<string>());
        Assert.Equal(2, local?["Shinano"]?["Deployment"]?["SuperUsers"]?.AsArray().Count);
        Assert.Equal(PASSWORD, local?["Shinano"]?["Monitor"]?["CertificatePassword"]?.GetValue<string>());
        Assert.Equal(panel.Token, local?["Shinano"]?["Web"]?["Token"]?.GetValue<string>());

        HttpResponseMessage cleared = await client.PutAsJsonAsync(
            "/api/config",
            new JsonObject { ["Monitor"] = new JsonObject { ["CertificatePassword"] = null } },
            Token);
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        local = JsonNode.Parse(await File.ReadAllTextAsync(panel.LocalConfigPath, Token));
        Assert.Null(local?["Shinano"]?["Monitor"]?["CertificatePassword"]);
    }

    [Fact]
    public async Task Stats_AggregateByDayConversationAndSender_ExcludingBotByDefault()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);
        long groupId = Random.Shared.NextInt64(1_000_000, 9_000_000);
        await ArchiveAsync(groupId, PluginHarness.MEMBER_ID, chars: 3);
        await ArchiveAsync(groupId, PluginHarness.MEMBER_ID, chars: 4);
        await ArchiveAsync(groupId, PluginHarness.ADMIN_ID, chars: 5);
        await ArchiveAsync(groupId, InfrastructureFixture.SELF_ID, chars: 100);

        JsonArray? daily = await client.GetFromJsonAsync<JsonArray>(
            $"/api/stats/daily?scope=Group&conversationId={groupId}",
            Token);
        JsonNode? today = Assert.Single(daily ?? []);
        Assert.Equal(3, today?["messages"]?.GetValue<int>());
        Assert.Equal(12, today?["chars"]?.GetValue<int>());

        JsonArray? conversations = await client.GetFromJsonAsync<JsonArray>("/api/stats/conversations?scope=Group&includeBot=true", Token);
        JsonNode? conversation = conversations?.Single(item => item?["conversationId"]?.GetValue<long>() == groupId);
        Assert.Equal(4, conversation?["messages"]?.GetValue<int>());
        Assert.Equal(3, conversation?["senders"]?.GetValue<int>());
        Assert.Equal("Group", conversation?["type"]?.GetValue<string>());

        JsonArray? senders = await client.GetFromJsonAsync<JsonArray>(
            $"/api/stats/senders?scope=Group&conversationId={groupId}",
            Token);
        Assert.Equal([PluginHarness.MEMBER_ID, PluginHarness.ADMIN_ID], senders?.Select(item => item?["senderId"]?.GetValue<long>() ?? 0));

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/archive-gaps", Token)).StatusCode);
    }

    private async Task ArchiveAsync(long groupId, long senderId, int chars) =>
        Assert.NotNull(
            await _fixture.Ready.ChatArchive.InsertAsync(
                InfrastructureFixture.SELF_ID,
                new ArchivedChatMessage
                {
                    SentAt           = DateTimeOffset.UtcNow,
                    StatDate         = ChatPartitionMaintainer.LocalDate(DateTimeOffset.UtcNow, _fixture.Ready.TimeZone),
                    ConversationType = ChatConversationType.Group,
                    ConversationId   = groupId,
                    SenderId         = senderId,
                    MessageId        = Random.Shared.NextInt64(1, long.MaxValue),
                    Text             = new string('字', chars),
                    Segments         = null,
                    Flags            = ChatArchiveFlags.None,
                    Counts           = new ChatMessageCounts { CharCount = chars }
                },
                Token));

    [Fact]
    public async Task PluginOrder_WithDuplicateOrNullIds_IsRejectedAndKeepsOrder()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);
        string[] before = await PluginIdsAsync(client);

        HttpResponseMessage duplicate = await client.PutAsJsonAsync("/api/plugins/order", new[] { PLUGIN_B, PLUGIN_A, PLUGIN_B }, Token);
        HttpResponseMessage withNull = await client.PutAsJsonAsync("/api/plugins/order", new[] { PLUGIN_B, null }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withNull.StatusCode);
        Assert.Equal(before, await PluginIdsAsync(client));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync("/api/plugins/order", before.Reverse(), Token)).StatusCode);
        Assert.Equal(before.Reverse(), await PluginIdsAsync(client));
    }

    private static async Task<string[]> PluginIdsAsync(HttpClient client) =>
        [.. (await client.GetFromJsonAsync<JsonArray>("/api/plugins", Token) ?? []).Select(plugin => plugin?["id"]?.GetValue<string>() ?? string.Empty)];

    [Fact]
    public async Task PluginPackage_IsUploadedListedAndUndone()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);

        using MultipartFormDataContent invalid = new() { { new ByteArrayContent([1, 2, 3]), "file", "broken.zip" } };
        HttpResponseMessage rejected = await client.PostAsync("/api/packages", invalid, Token);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await using Stream zip = Restart.PackageFixture.Zip(Restart.PackageFixture.Package("sample-c", "1.2.0"));
        using MultipartFormDataContent upload = new() { { new StreamContent(zip), "file", "sample-c.zip" } };
        HttpResponseMessage staged = await client.PostAsync("/api/packages", upload, Token);
        Assert.Equal(HttpStatusCode.OK, staged.StatusCode);

        JsonObject? status = await client.GetFromJsonAsync<JsonObject>("/api/restart", Token);
        JsonNode? change = Assert.Single(status?["pending"]?.AsArray() ?? [])?["change"];
        Assert.Equal("Install", change?["kind"]?.GetValue<string>());
        Assert.Equal("1.2.0", change?["toVersion"]?.GetValue<string>());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/api/packages/sample-c", Token)).StatusCode);
        status = await client.GetFromJsonAsync<JsonObject>("/api/restart", Token);
        Assert.Empty(status?["pending"]?.AsArray() ?? []);
    }

    [Fact]
    public async Task PluginPackageRemoval_WithPurge_PreviewsDataAndIsStagedWithFlag()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);
        string installed = Path.Combine(Path.GetDirectoryName(panel.LocalConfigPath) ?? string.Empty, "plugins", "SampleD");
        Directory.CreateDirectory(installed);
        await File.WriteAllTextAsync(
            Path.Combine(installed, "plugin.json"),
            """{ "id": "sample-d", "version": "1.0.0", "entryAssembly": "Sample.dll", "bucketPurposes": ["sampled"], "tables": { "global": ["sample_d_note"] } }""",
            Token);
        await File.WriteAllTextAsync(Path.Combine(installed, "Sample.dll"), "1.0.0", Token);

        JsonObject? preview = await client.GetFromJsonAsync<JsonObject>("/api/packages/sample-d/data", Token);
        Assert.Equal("sample_d_note", Assert.Single(preview?["tables"]?["global"]?.AsArray() ?? [])?.GetValue<string>());
        Assert.Empty(preview?["tables"]?["account"]?.AsArray() ?? [""]);
        Assert.Equal($"{_fixture.BucketPrefix}-sampled", Assert.Single(preview?["buckets"]?.AsArray() ?? [])?.GetValue<string>());
        Assert.False(preview?["secretsFile"]?.GetValue<bool>());

        HttpResponseMessage removed = await client.PostAsJsonAsync("/api/packages/sample-d/remove", new { purgeData = true }, Token);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        JsonObject? status = await client.GetFromJsonAsync<JsonObject>("/api/restart", Token);
        JsonNode? change = Assert.Single(status?["pending"]?.AsArray() ?? [])?["change"];
        Assert.Equal("Remove", change?["kind"]?.GetValue<string>());
        Assert.True(change?["purgeData"]?.GetValue<bool>());

        // 清除只在重启确认成功后建立，暂存时没有清除结果，也不能关闭不存在的结果。
        Assert.Empty(await client.GetFromJsonAsync<JsonArray>("/api/plugin-purges", Token) ?? [null]);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync("/api/plugin-purges/sample-d", Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/packages/missing/data", Token)).StatusCode);
    }

    private Task<PanelHarness> StartAsync() =>
        PanelHarness.StartAsync(_fixture.Ready, _fixture.BucketPrefix, TEST_PLUGINS, PLUGIN_A, PLUGIN_B);

    private static async Task<HttpClient> LoggedInClientAsync(PanelHarness panel)
    {
        HttpClient client = panel.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await LoginAsync(client, panel.Token)).StatusCode);
        return client;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient client, string? token) =>
        client.PostAsJsonAsync("/api/auth/login", new { token }, Token);
}
