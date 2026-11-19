namespace Shinano.Infrastructure.ChatArchive;

/// <summary>由一条消息记录得到的归档内容，以及写入后仍需在后台完成的媒体与转发归档。</summary>
public sealed class ChatArchiveEntry
{
    /// <summary>机器人账号。</summary>
    public required long SelfId { get; init; }

    /// <summary>是否为 Bot 发送的消息。</summary>
    public required bool IsOutbound { get; init; }

    /// <summary>写入聊天表的内容。</summary>
    public required ArchivedChatMessage Message { get; init; }

    /// <summary>编码器，持有待归档的媒体与待展开的转发。</summary>
    public required ChatSegmentEncoder Encoder { get; init; }

    /// <summary>写入后得到的行 ID。</summary>
    public long RowId { get; set; }

    /// <summary>是否有需要后台完成的媒体或转发。</summary>
    public bool HasPendingWork => Encoder.PendingMedia.Count > 0 || Encoder.PendingForwards.Count > 0;
}
