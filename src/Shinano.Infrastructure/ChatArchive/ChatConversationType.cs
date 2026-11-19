namespace Shinano.Infrastructure.ChatArchive;

/// <summary>聊天记录的会话类型，以 smallint 存储，取值不可更改。</summary>
public enum ChatConversationType : short
{
    /// <summary>好友私聊，会话 ID 为对方 QQ 号。</summary>
    Friend = 1,

    /// <summary>群聊，会话 ID 为群号。</summary>
    Group = 2,

    /// <summary>群临时会话，会话 ID 为对方 QQ 号。</summary>
    Temp = 3
}
