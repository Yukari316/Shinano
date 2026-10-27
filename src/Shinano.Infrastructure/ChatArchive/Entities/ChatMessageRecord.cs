using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Database;
using SqlSugar;

namespace Shinano.Infrastructure.ChatArchive.Entities;

/// <summary>
/// 账号 schema 中聊天表的一行。表是按日分区的父表，由 <see cref="ChatMessageTable" /> 以显式 SQL 创建，
/// 不得交给 CodeFirst。
/// </summary>
[SugarTable(ChatMessageTable.TABLE_NAME)]
public sealed class ChatMessageRecord
{
    /// <summary>行 ID，由数据库生成。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>消息时间，UTC，也是分区键。</summary>
    [SugarColumn(ColumnName = "sent_at")]
    public DateTimeOffset SentAt { get; set; }

    /// <summary>会话类型，取值见 <see cref="ChatConversationType" />。</summary>
    [SugarColumn(ColumnName = "conversation_type")]
    public short ConversationType { get; set; }

    /// <summary>会话 ID。</summary>
    [SugarColumn(ColumnName = "conversation_id")]
    public long ConversationId { get; set; }

    /// <summary>发言者。</summary>
    [SugarColumn(ColumnName = "sender_id")]
    public long SenderId { get; set; }

    /// <summary>协议消息 ID。</summary>
    [SugarColumn(ColumnName = "message_id")]
    public long MessageId { get; set; }

    /// <summary>可见文本。</summary>
    [SugarColumn(ColumnName = "text", IsNullable = true)]
    public string? Text { get; set; }

    /// <summary>精简段 JSON。</summary>
    [SugarColumn(ColumnName = "segments", IsNullable = true, SqlParameterDbType = typeof(JsonbStringConverter))]
    public string? Segments { get; set; }

    /// <summary>归档状态，取值见 <see cref="ChatArchiveFlags" />。</summary>
    [SugarColumn(ColumnName = "archive_flags")]
    public short ArchiveFlags { get; set; }
}
