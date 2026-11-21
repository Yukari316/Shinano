namespace Shinano.Plugin.Gemini.Persistence;

/// <summary>Gemini 响应范围的会话类别。</summary>
public enum GeminiScopeKind
{
    /// <summary>群聊，按群号匹配。</summary>
    Group = 1,

    /// <summary>好友私聊，按好友 QQ 号匹配。</summary>
    Private = 2
}
