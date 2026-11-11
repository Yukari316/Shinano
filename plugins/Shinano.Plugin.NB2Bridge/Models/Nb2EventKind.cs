namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>交付给 NB2 的事件类别，对应插件配置中的四个开关。</summary>
public enum Nb2EventKind
{
    /// <summary>群聊消息。</summary>
    GroupMessage,

    /// <summary>好友私聊消息。临时会话不交付。</summary>
    PrivateMessage,

    /// <summary>通知事件。</summary>
    Notice,

    /// <summary>请求事件。</summary>
    Request
}
