namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>交付给 NB2 的事件类别，对应插件配置中的四个开关。</summary>
public enum Nb2EventKind
{
    /// <summary>群聊消息。</summary>
    GroupMessage,

    /// <summary>私聊消息（好友与临时会话）。</summary>
    PrivateMessage,

    /// <summary>通知事件。</summary>
    Notice,

    /// <summary>请求事件。</summary>
    Request
}
