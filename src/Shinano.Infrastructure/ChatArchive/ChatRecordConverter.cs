using System.Text.Json.Nodes;
using Shinano.Infrastructure.ChatArchive.Persistence;
using Sora.Core.Enums;
using Sora.Entities.Recording;
using Sora.Entities.Segments;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>把 Sora 消息记录转换为归档内容。</summary>
public static class ChatRecordConverter
{
    /// <summary>转换一条记录；不支持的会话来源返回 null。</summary>
    /// <param name="record">消息记录。</param>
    /// <param name="timeZone">统计日期使用的时区。</param>
    public static ChatArchiveEntry? Convert(MessageRecord record, TimeZoneInfo timeZone)
    {
        ChatConversationType? type = record.SourceType switch
        {
            MessageSourceType.Group  => ChatConversationType.Group,
            MessageSourceType.Friend => ChatConversationType.Friend,
            MessageSourceType.Temp   => ChatConversationType.Temp,
            _                        => null
        };
        if (type is null) return null;

        // Sora 的记录时间为本地时间（Kind=Local）。
        DateTimeOffset sentAt = new DateTimeOffset(record.Time).ToUniversalTime();
        bool textOnly = record.Segments.All(segment => segment is TextSegment);
        string[] texts = [.. record.Segments.OfType<TextSegment>().Select(segment => segment.Text)];
        ChatSegmentEncoder encoder = new();
        JsonArray? segments = textOnly ? null : encoder.Encode(record.Segments, depth: 0);

        ChatArchiveFlags flags = ChatArchiveFlags.None;
        if (encoder.PendingMedia.Count > 0 || encoder.PendingForwards.Count > 0) flags |= ChatArchiveFlags.MediaPending;
        if (encoder.HasMissingContent) flags |= ChatArchiveFlags.ContentMissing;

        return new ChatArchiveEntry
        {
            SelfId     = record.SelfId,
            IsOutbound = record.Direction == RecordDirection.Outbound,
            Encoder    = encoder,
            Message = new ArchivedChatMessage
            {
                SentAt           = sentAt,
                StatDate         = ChatPartitionMaintainer.LocalDate(sentAt, timeZone),
                ConversationType = type.Value,
                ConversationId   = type == ChatConversationType.Group ? record.GroupId : record.PeerUserId,
                SenderId         = record.SenderId,
                MessageId        = record.MessageId,
                Text             = texts.Length == 0 ? null : string.Concat(texts),
                Segments         = segments,
                Flags            = flags,
                Counts           = ChatMessageCounts.FromSegments(record.Segments)
            }
        };
    }
}
