using Shinano.Plugin.OnlineCheck;
using Shinano.Tests.Host.Plugins;
using Shinano.Tests.Support;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Tests.Plugins;

[Collection(InfrastructureCollection.NAME)]
public sealed class OnlineCheckPluginTests
{
    private const string PLUGINS   = "artifacts/plugins";
    private const string PLUGIN_ID = "online-check";

    private readonly InfrastructureFixture _fixture;

    public OnlineCheckPluginTests(InfrastructureFixture fixture)
    {
        _fixture = fixture;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Package_RespondsToPresenceNudgeAndEcho()
    {
        await using PluginHarness harness =
            await PluginHarness.StartAsync(_fixture.Ready, _fixture.BucketPrefix, PLUGINS, PLUGIN_ID);
        FakeMilkyServer server = harness.Server;
        Assert.Null(await harness.Runtime.EnableAsync(PLUGIN_ID, Token));

        await server.PushFriendMessageAsync(PluginHarness.MEMBER_ID, "在");
        SentMessage presence = await harness.ExpectReplyAsync(string.Empty);
        Assert.Contains("base64://", presence.Raw);

        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "在");
        await harness.ExpectSilenceAsync();

        await server.PushGroupNudgeAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, server.SelfId);
        SentMessage poke = await harness.ExpectReplyAsync("\r\n你今晚必被爽哥杀害\r\n");
        Assert.Equal(3, poke.SegmentCount);
        await server.PushGroupNudgeAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, server.SelfId);
        await harness.ExpectReplyAsync("再戳？再戳把你牙拔了当球踢");

        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.MEMBER_ID, "member", "echo hi");
        await harness.ExpectSilenceAsync();
        await server.PushGroupMessageAsync(PluginHarness.GROUP_ID, PluginHarness.SUPER_USER_ID, "member", "echo hi there");
        await harness.ExpectReplyAsync("hi there");
        await server.PushFriendMessageAsync(PluginHarness.SUPER_USER_ID, "echo hi");
        await harness.ExpectSilenceAsync();
    }

    [Fact]
    public void StripEchoPrefix_RemovesExactlyPrefixAndOneWhitespace()
    {
        MessageBody body = OnlineCheckCommands.StripEchoPrefix(new MessageBody("echo hello world"));
        Assert.Equal("hello world", body.GetText());

        MessageBody imageOnly = OnlineCheckCommands.StripEchoPrefix(
            new MessageBody([new TextSegment { Text = "echo " }, SegmentBuilder.Image("base64://AA==")]));
        Assert.IsType<ImageSegment>(Assert.Single(imageOnly));
    }
}
