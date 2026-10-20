namespace Shinano.Host.Panel;

/// <summary>统计查询条件。日期为配置时区的自然日，两端都包含。</summary>
public sealed class ChatStatFilter
{
    /// <summary>机器人账号。</summary>
    public required long SelfId { get; init; }

    /// <summary>起始日期。</summary>
    public required DateOnly From { get; init; }

    /// <summary>结束日期。</summary>
    public required DateOnly To { get; init; }

    /// <summary>会话范围。</summary>
    public required ChatStatScope Scope { get; init; }

    /// <summary>只统计某个会话；为 null 时不限。</summary>
    public long? ConversationId { get; init; }

    /// <summary>是否包含 Bot 自己发送的消息。</summary>
    public required bool IncludeBot { get; init; }
}
