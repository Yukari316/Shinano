using Shinano.Framework.Plugins.Capabilities;
using Shinano.Host.Plugins.Capabilities.Models;
using Sora.Entities.Events;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>收集插件在初始化期间登记的事件订阅；初始化结束后不再接受新订阅。</summary>
internal sealed class PluginEvents : IPluginEvents
{
    private readonly List<PluginEventSubscription> _subscriptions = [];
    private volatile bool                          _sealed;

    /// <summary>已登记的订阅，初始化结束后只读。</summary>
    public IReadOnlyList<PluginEventSubscription> Subscriptions => _subscriptions;

    /// <inheritdoc />
    public void Subscribe<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : BotEvent
    {
        if (_sealed) throw new InvalidOperationException("事件订阅只能在插件初始化期间登记");
        _subscriptions.Add(
            new PluginEventSubscription
            {
                EventType = typeof(TEvent),
                Handler   = e => handler((TEvent)e)
            });
    }

    /// <summary>结束登记。</summary>
    public void Seal() => _sealed = true;
}
