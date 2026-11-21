using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Entities.Events;

namespace Shinano.Tests.SamplePluginA;

/// <summary>测试指令。</summary>
[CommandGroup(Name = "sample-a")]
public sealed class SampleCommands
{
    private readonly Greeter                      _greeter;
    private readonly IPluginInitializationContext _context;

    /// <summary>创建指令组。</summary>
    public SampleCommands(Greeter greeter, IPluginInitializationContext context)
    {
        _greeter = greeter;
        _context = context;
    }

    /// <summary>回复插件标识，用于验证跨插件顺序。</summary>
    [Command(Expressions = ["order"])]
    public async ValueTask OrderAsync(MessageReceivedEvent e) => await e.ReplyAsync(_greeter.Name);

    /// <summary>回复配置中的问候语，用于验证面板保存后插件读到新配置。</summary>
    [Command(Expressions = ["config-a"])]
    public async ValueTask ConfigAsync(MessageReceivedEvent e) =>
        await e.ReplyAsync(_context.Config<SampleConfig>().Value.Greeting);

    /// <summary>回复机密密钥的长度，用于验证面板保存后插件立即读到新机密。</summary>
    [Command(Expressions = ["secret-a"])]
    public async ValueTask SecretAsync(MessageReceivedEvent e) =>
        await e.ReplyAsync($"secret-a={_context.Secrets<SampleSecrets>().Value.ApiKey?.Length ?? 0}");

    /// <summary>回复初始化次数。</summary>
    [Command(Expressions = ["init-count-a"])]
    public async ValueTask InitCountAsync(MessageReceivedEvent e) =>
        await e.ReplyAsync($"init-a={SamplePlugin.InitializeCount}");

    /// <summary>要求群管理员。</summary>
    [Command(Expressions = ["admin-a"])]
    [RequirePermission(MemberRole.Admin)]
    public async ValueTask AdminAsync(MessageReceivedEvent e) => await e.ReplyAsync("ok-a");

    /// <summary>要求群管理员且有冷却，验证权限过滤器先于冷却。</summary>
    /// <remarks>
    /// 关闭防重入：测试在收到回复后立即发送下一条，此时上一次执行可能尚未释放重入占用，消息会被静默拒绝。
    /// </remarks>
    [Command(Expressions = ["cooldown-a"], PreventReentry = false)]
    [Cooldown(60, Reply = "cd-a")]
    [RequirePermission(MemberRole.Admin)]
    public async ValueTask CooldownAsync(MessageReceivedEvent e) => await e.ReplyAsync("ok-a");

    /// <summary>按会话限流，每个会话 60 秒内只放行一次。</summary>
    [Command(Expressions = ["ratelimit-a"], PreventReentry = false)]
    [RateLimit(1, 60, Scope = RateLimitScope.Session, Reply = "rl-a")]
    public async ValueTask RateLimitAsync(MessageReceivedEvent e) => await e.ReplyAsync("ok-rl");
}
