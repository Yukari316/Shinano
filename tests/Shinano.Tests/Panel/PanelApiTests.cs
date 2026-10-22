using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Shinano.Host.Panel;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Tests.Infrastructure;
using Shinano.Tests.Plugins;

namespace Shinano.Tests.Panel;

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
    public async Task HostConfig_HidesSecrets_ValidatesAndWritesLocalConfig()
    {
        await using PanelHarness panel = await StartAsync();
        using HttpClient client = await LoggedInClientAsync(panel);

        JsonArray? fields = await client.GetFromJsonAsync<JsonArray>("/api/config", Token);
        JsonNode? token = fields?.Single(item => item?["field"]?["key"]?.GetValue<string>() == "Token");
        Assert.True(token?["value"]?.GetValue<bool>());
        Assert.DoesNotContain(panel.Token, fields?.ToJsonString() ?? string.Empty);

        HttpResponseMessage invalid = await client.PutAsJsonAsync(
            "/api/config",
            new JsonObject
            {
                ["LlBot"] = new JsonObject { ["Port"] = 70000 },
                ["Web"]   = new JsonObject { ["Token"] = "x" }
            },
            Token);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(2, (await invalid.Content.ReadFromJsonAsync<JsonObject>(Token))?["errors"]?.AsArray().Count);

        HttpResponseMessage saved = await client.PutAsJsonAsync(
            "/api/config",
            new JsonObject
            {
                ["LlBot"]      = new JsonObject { ["Port"] = 3015 },
                ["Deployment"] = new JsonObject { ["SuperUsers"] = new JsonArray(1, 2) }
            },
            Token);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        JsonNode? local = JsonNode.Parse(await File.ReadAllTextAsync(panel.LocalConfigPath, Token));
        Assert.Equal(3015, local?["Shinano"]?["LlBot"]?["Port"]?.GetValue<int>());
        Assert.Equal(2, local?["Shinano"]?["Deployment"]?["SuperUsers"]?.AsArray().Count);
        Assert.Equal(panel.Token, local?["Shinano"]?["Web"]?["Token"]?.GetValue<string>());
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
