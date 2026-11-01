using Shinano.Framework.Commands;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Entities.Events;

namespace Shinano.Tests.SamplePluginB;

/// <summary>测试指令。</summary>
[CommandGroup(Name = "sample-b")]
public sealed class SampleCommands
{
    private readonly Greeter _greeter;

    /// <summary>创建指令组。</summary>
    public SampleCommands(Greeter greeter)
    {
        _greeter = greeter;
    }

    /// <summary>回复插件标识，用于验证跨插件顺序。</summary>
    [Command(Expressions = ["order"])]
    public async ValueTask OrderAsync(MessageReceivedEvent e) => await e.ReplyAsync(_greeter.Name);

    /// <summary>回复初始化次数。</summary>
    [Command(Expressions = ["init-count-b"])]
    public async ValueTask InitCountAsync(MessageReceivedEvent e) =>
        await e.ReplyAsync($"init-b={SamplePlugin.InitializeCount}");

    /// <summary>要求群管理员。</summary>
    [Command(Expressions = ["admin-b"])]
    [RequirePermission(MemberRole.Admin)]
    public async ValueTask AdminAsync(MessageReceivedEvent e) => await e.ReplyAsync("ok-b");
}
