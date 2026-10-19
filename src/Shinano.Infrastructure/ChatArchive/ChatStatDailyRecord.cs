using Shinano.Infrastructure.Database;
using SqlSugar;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>
/// 账号 schema 中的发言统计，按"统计日期 × 会话 × 发言者"聚合，日期使用配置时区。
/// 统计独立于聊天表持久化，聊天分区过期不影响统计。
/// </summary>
[SugarTable("chat_stat_daily")]
public sealed class ChatStatDailyRecord
{
    /// <summary>统计日期。</summary>
    [SugarColumn(ColumnName = "stat_date", IsPrimaryKey = true, ColumnDataType = "date", SqlParameterDbType = typeof(DateOnlySqlConverter))]
    public DateOnly StatDate { get; set; }

    /// <summary>会话类型，取值见 <see cref="ChatConversationType" />。</summary>
    [SugarColumn(ColumnName = "conversation_type", IsPrimaryKey = true)]
    public short ConversationType { get; set; }

    /// <summary>会话 ID。</summary>
    [SugarColumn(ColumnName = "conversation_id", IsPrimaryKey = true)]
    public long ConversationId { get; set; }

    /// <summary>发言者。</summary>
    [SugarColumn(ColumnName = "sender_id", IsPrimaryKey = true)]
    public long SenderId { get; set; }

    /// <summary>消息条数。</summary>
    [SugarColumn(ColumnName = "message_count")]
    public int MessageCount { get; set; }

    /// <summary>可见字数。</summary>
    [SugarColumn(ColumnName = "char_count")]
    public int CharCount { get; set; }

    /// <summary>图片数。</summary>
    [SugarColumn(ColumnName = "image_count")]
    public int ImageCount { get; set; }

    /// <summary>语音数。</summary>
    [SugarColumn(ColumnName = "audio_count")]
    public int AudioCount { get; set; }

    /// <summary>视频数。</summary>
    [SugarColumn(ColumnName = "video_count")]
    public int VideoCount { get; set; }

    /// <summary>文件数。</summary>
    [SugarColumn(ColumnName = "file_count")]
    public int FileCount { get; set; }
}
