using Sora.Entities.Events;

namespace Shinano.Framework.Plugins.Capabilities;

/// <summary>
/// 普通事件订阅。宿主是 Sora 事件的唯一订阅者，按插件顺序依次调用各插件的订阅；
/// 某个处理器把 <see cref="BotEvent.IsContinueEventChain" /> 设为 false 后，后续订阅者不再执行。
/// 指令先于普通事件处理，已被指令阻断的消息不会到达这里。
/// </summary>
public interface IPluginEvents
{
    /// <summary>
    /// 订阅 <typeparamref name="TEvent" /> 及其派生事件。只能在 <see cref="IShinanoPlugin.InitializeAsync" /> 中调用。
    /// 处理器抛出的异常记录日志后忽略。
    /// </summary>
    void Subscribe<TEvent>(Func<TEvent, ValueTask> handler) where TEvent : BotEvent;
}
