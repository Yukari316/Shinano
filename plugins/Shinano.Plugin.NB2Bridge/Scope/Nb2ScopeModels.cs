namespace Shinano.Plugin.NB2Bridge.Scope;

/// <summary>群聊与私聊的响应范围。</summary>
public sealed class Nb2ScopesView
{
    /// <summary>群聊。</summary>
    public required Nb2ScopeView Group { get; init; }

    /// <summary>好友私聊。</summary>
    public required Nb2ScopeView Private { get; init; }
}

/// <summary>一类会话的响应范围。</summary>
public sealed class Nb2ScopeView
{
    /// <summary>为 true 时为白名单，否则为黑名单。</summary>
    public required bool Whitelist { get; init; }

    /// <summary>群号或好友 QQ 号，升序。</summary>
    public required long[] Ids { get; init; }
}

/// <summary>保存一类会话响应范围的请求。</summary>
public sealed class Nb2ScopeUpdate
{
    /// <summary>为 true 时为白名单，否则为黑名单。</summary>
    public bool Whitelist { get; init; }

    /// <summary>群号或好友 QQ 号，须为正数，重复的号码合并。</summary>
    public long[] Ids { get; init; } = [];
}

/// <summary>选择范围时的候选会话。</summary>
public sealed class Nb2ScopeCandidateView
{
    /// <summary>群号或好友 QQ 号。</summary>
    public required long Id { get; init; }

    /// <summary>群名或好友备注（没有备注时为昵称）；Bot 已不在该群或已不是好友时为 null。</summary>
    public string? Name { get; init; }
}
