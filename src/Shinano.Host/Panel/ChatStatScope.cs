namespace Shinano.Host.Panel;

/// <summary>统计查询的会话范围。</summary>
public enum ChatStatScope
{
    /// <summary>全部会话。</summary>
    All,

    /// <summary>群聊。</summary>
    Group,

    /// <summary>私聊：好友私聊与群临时会话。</summary>
    Private
}
