namespace Shinano.Plugin.NB2Bridge.Scope;

/// <summary>NB2 响应范围的会话类别，各自有独立的模式与名单。值写入数据库，不得改动。</summary>
public enum Nb2ScopeKind
{
    /// <summary>群聊，名单为群号。</summary>
    Group = 0,

    /// <summary>好友私聊，名单为好友 QQ 号。</summary>
    Private = 1
}
