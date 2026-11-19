using Autofac;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Plugin.OnlineCheck;

/// <summary>在线检测插件：私聊"在"、群内戳 Bot 与 echo，用于确认 Bot 在线。</summary>
public sealed class OnlineCheckPlugin : IShinanoPlugin
{
    private const string POKE_REPLY     = "你今晚必被爽哥杀害";
    private const string COOLDOWN_REPLY = "再戳？再戳把你牙拔了当球踢";

    private readonly OnlineCheckAssets                 _assets   = new();
    private readonly CooldownTracker                   _cooldown = new(TimeProvider.System);
    private          IPluginConfig<OnlineCheckConfig>? _config;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder) => builder.RegisterInstance(_assets);

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel) => panel.UseConfig<OnlineCheckConfig>();

    /// <inheritdoc />
    public async ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        _config = context.Config<OnlineCheckConfig>();
        await _assets.LoadAsync(context.Assets, cancellationToken);
        context.Events.Subscribe<NudgeEvent>(OnNudgeAsync);
    }

    private async ValueTask OnNudgeAsync(NudgeEvent e)
    {
        if (e.SourceType != MessageSourceType.Group || e.ReceiverId != e.SelfId) return;

        // SAFE: 订阅在 InitializeAsync 设置 _config 之后才会被调用。
        TimeSpan cooldown = TimeSpan.FromSeconds(_config!.Value.PokeCooldownSeconds);
        MessageBody reply = _cooldown.Trigger(e.GroupId, e.SenderId, cooldown)
            ? new MessageBody(COOLDOWN_REPLY)
            : SegmentBuilder.Mention(e.SenderId) + $"\r\n{POKE_REPLY}\r\n" + SegmentBuilder.Image(_assets.PokeImageUri);
        await e.Api.SendGroupMessageAsync(e.GroupId, reply);
        e.IsContinueEventChain = false;
    }
}
