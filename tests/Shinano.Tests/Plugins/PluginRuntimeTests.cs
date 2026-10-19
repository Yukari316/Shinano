using Shinano.Tests.Infrastructure;

namespace Shinano.Tests.Plugins;

[Collection(InfrastructureCollection.NAME)]
public sealed class PluginRuntimeTests
{
    private const string TEST_PLUGINS = "artifacts/test-plugins";
    private const string PLUGIN_A     = "sample-a";
    private const string PLUGIN_B     = "sample-b";

    private readonly InfrastructureFixture _fixture;

    public PluginRuntimeTests(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EnableDisableReenable_FollowsPluginOrder_WithoutReinitializing()
    {
        await using PluginHarness harness = await StartAsync();
        FakeMilkyServer server = harness.Server;

        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_B, Token));
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "order");
        await harness.ExpectReplyAsync("b");

        // A 排在前面，优先级更高，且指令匹配后阻断后续插件。
        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_A, Token));
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "order");
        await harness.ExpectReplyAsync("a");
        await harness.ExpectSilenceAsync();

        // 停用的插件不占用消息，后续插件照常响应。
        Assert.Null(await harness.Runtime.DisableAsync(PLUGIN_A, Token));
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "order");
        await harness.ExpectReplyAsync("b");

        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_A, Token));
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "order");
        await harness.ExpectReplyAsync("a");
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "init-count-a");
        await harness.ExpectReplyAsync("init-a=1");

        await harness.Runtime.SetOrderAsync([PLUGIN_B, PLUGIN_A], Token);
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "order");
        await harness.ExpectReplyAsync("b");
    }

    [Fact]
    public async Task EventChain_FollowsPluginOrder_AndStopsWhenHandlerEndsChain()
    {
        await using PluginHarness harness = await StartAsync();
        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_A, Token));
        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_B, Token));

        await harness.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "hello");
        await harness.ExpectReplyAsync("world-a");
        await harness.ExpectSilenceAsync();

        await harness.Runtime.SetOrderAsync([PLUGIN_B, PLUGIN_A], Token);
        await harness.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "hello");
        await harness.ExpectReplyAsync("world-b");
        await harness.ExpectReplyAsync("world-a");
    }

    [Fact]
    public async Task RequirePermission_SuperUserFirst_ThenGroupRole_PrivateRejectsOthers()
    {
        await using PluginHarness harness = await StartAsync();
        FakeMilkyServer server = harness.Server;
        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_A, Token));

        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "admin-a");
        await harness.ExpectSilenceAsync();
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.ADMIN_ID, "admin", "admin-a");
        await harness.ExpectReplyAsync("ok-a");
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.SUPER_USER_ID, "member", "admin-a");
        await harness.ExpectReplyAsync("ok-a");
        await server.PushFriendMessageAsync(PluginHarness.ADMIN_ID, "admin-a");
        await harness.ExpectSilenceAsync();
        await server.PushFriendMessageAsync(PluginHarness.SUPER_USER_ID, "admin-a");
        await harness.ExpectReplyAsync("ok-a");
    }

    [Fact]
    public async Task Cooldown_PerGroupAndSender_AfterPermission_NoSuperUserExemption()
    {
        await using PluginHarness harness = await StartAsync();
        FakeMilkyServer server = harness.Server;
        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_A, Token));

        // 冷却特性声明在权限特性之前，仍按 Order 在权限之后执行：被拒绝的成员不进入冷却。
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "cooldown-a");
        await harness.ExpectSilenceAsync();
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "admin", "cooldown-a");
        await harness.ExpectReplyAsync("ok-a");
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "admin", "cooldown-a");
        await harness.ExpectReplyAsync("cd-a");

        await server.PushGroupMessageAsync(PluginHarness.OTHER_GROUP_ID, PluginHarness.MEMBER_ID, "admin", "cooldown-a");
        await harness.ExpectReplyAsync("ok-a");
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.SUPER_USER_ID, "member", "cooldown-a");
        await harness.ExpectReplyAsync("ok-a");
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.SUPER_USER_ID, "member", "cooldown-a");
        await harness.ExpectReplyAsync("cd-a");
    }

    [Fact]
    public async Task Suspend_StopsAllPlugins_AndResumeRestoresEnabledState()
    {
        await using PluginHarness harness = await StartAsync();
        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_A, Token));

        harness.Runtime.Suspend();
        await harness.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "order");
        await harness.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "hello");
        await harness.ExpectSilenceAsync();
        Assert.NotNull(await harness.Runtime.EnableAsync(PLUGIN_B, Token));

        harness.Runtime.Resume();
        await harness.Server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "order");
        await harness.ExpectReplyAsync("a");
    }

    private Task<PluginHarness> StartAsync() =>
        PluginHarness.StartAsync(_fixture.Ready, _fixture.BucketPrefix, TEST_PLUGINS, PLUGIN_A, PLUGIN_B);
}
