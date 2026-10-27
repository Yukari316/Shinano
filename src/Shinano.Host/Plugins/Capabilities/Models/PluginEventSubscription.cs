using Sora.Entities.Events;

namespace Shinano.Host.Plugins.Capabilities.Models;

/// <summary>插件登记的一个普通事件订阅。</summary>
internal sealed class PluginEventSubscription
{
    /// <summary>订阅的事件类型，派生事件同样匹配。</summary>
    public required Type EventType { get; init; }

    /// <summary>处理器。</summary>
    public required Func<BotEvent, ValueTask> Handler { get; init; }
}
