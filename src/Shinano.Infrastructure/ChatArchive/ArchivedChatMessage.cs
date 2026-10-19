using System.Text.Json.Nodes;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>写入聊天表的一条消息及其统计增量。</summary>
public sealed class ArchivedChatMessage
{
    /// <summary>消息时间，UTC。</summary>
    public required DateTimeOffset SentAt { get; init; }

    /// <summary>按配置时区计算的统计日期。</summary>
    public required DateOnly StatDate { get; init; }

    /// <summary>会话类型。</summary>
    public required ChatConversationType ConversationType { get; init; }

    /// <summary>会话 ID：群号或私聊对方 QQ 号。</summary>
    public required long ConversationId { get; init; }

    /// <summary>发言者 QQ 号；Bot 发送的消息为机器人账号。</summary>
    public required long SenderId { get; init; }

    /// <summary>协议消息 ID（Milky message_seq）。</summary>
    public required long MessageId { get; init; }

    /// <summary>顶层文本段拼接的可见文本；没有文本段时为 null。</summary>
    public required string? Text { get; init; }

    /// <summary>精简段 JSON；消息只含文本段时为 null。媒体归档完成前可能缺少媒体 ID。</summary>
    public required JsonArray? Segments { get; init; }

    /// <summary>写入时的归档状态。</summary>
    public required ChatArchiveFlags Flags { get; init; }

    /// <summary>计入发言统计的数量。</summary>
    public required ChatMessageCounts Counts { get; init; }
}
