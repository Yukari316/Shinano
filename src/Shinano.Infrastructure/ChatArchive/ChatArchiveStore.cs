using System.Text.Json.Nodes;
using Shinano.Infrastructure.ChatArchive.Entities;
using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Database;
using SqlSugar;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>账号 schema 中聊天记录、发言统计与归档缺口的读写。</summary>
public sealed class ChatArchiveStore
{
    private readonly ShinanoDatabase _database;

    /// <summary>创建存储。</summary>
    public ChatArchiveStore(ShinanoDatabase database)
    {
        _database = database;
    }

    /// <summary>创建账号的聊天表、统计表与缺口表；分区另由分区维护创建。</summary>
    public async Task EnsureTablesAsync(long selfId, CancellationToken cancellationToken)
    {
        await ChatMessageTable.EnsureAsync(_database, selfId, cancellationToken);
        _database.Account(selfId).CodeFirst.InitTables<ChatStatDailyRecord, ArchiveGapRecord>();
    }

    /// <summary>
    /// 在一个事务中写入一条消息并累加发言统计。消息以（会话类型、会话 ID、消息 ID、消息时间）去重，
    /// 重复的记录不写入也不重复统计。
    /// </summary>
    /// <remarks>
    /// 先查后写的去重与统计累加要求同一账号只有一个写入者，由宿主唯一的归档工作者保证；
    /// 并发写入同一消息时唯一约束会使后写入的事务失败。
    /// </remarks>
    /// <returns>新行 ID；重复记录返回 null。</returns>
    /// <exception cref="Npgsql.PostgresException">消息时间没有对应分区等服务端错误。</exception>
    /// <exception cref="SqlSugarException">无法连接数据库。</exception>
    public async Task<long?> InsertAsync(long selfId, ArchivedChatMessage message, CancellationToken cancellationToken)
    {
        SqlSugarScope db = _database.Account(selfId);
        short type = (short)message.ConversationType;
        return await db.InTransactionAsync(async () =>
        {
            bool exists = await db.Queryable<ChatMessageRecord>()
                                  .Where(row => row.ConversationType == type
                                                && row.ConversationId == message.ConversationId
                                                && row.MessageId == message.MessageId
                                                && row.SentAt == message.SentAt)
                                  .AnyAsync(cancellationToken);
            if (exists) return (long?)null;

            long id = await db.Insertable(
                                  new ChatMessageRecord
                                  {
                                      SentAt           = message.SentAt,
                                      ConversationType = type,
                                      ConversationId   = message.ConversationId,
                                      SenderId         = message.SenderId,
                                      MessageId        = message.MessageId,
                                      Text             = message.Text,
                                      Segments         = message.Segments?.ToJsonString(),
                                      ArchiveFlags     = (short)message.Flags
                                  })
                              .ExecuteReturnBigIdentityAsync(cancellationToken);
            await CountAsync(db, message);
            return id;
        });
    }

    /// <summary>回写后台归档完成后的段 JSON 与状态；所在分区已删除时不做任何事。</summary>
    public async Task UpdateSegmentsAsync(
        long              selfId,
        long              rowId,
        DateTimeOffset    sentAt,
        JsonArray?        segments,
        ChatArchiveFlags  flags,
        CancellationToken cancellationToken) =>
        // 以实体更新使 jsonb 列经 JsonbStringConverter 传参；按 id 与分区键定位行。
        await _database.Account(selfId)
                       .Updateable(
                           new ChatMessageRecord
                           {
                               Id           = rowId,
                               SentAt       = sentAt,
                               Segments     = segments?.ToJsonString(),
                               ArchiveFlags = (short)flags
                           })
                       .UpdateColumns(row => new { row.Segments, row.ArchiveFlags })
                       .WhereColumns(row => new { row.Id, row.SentAt })
                       .ExecuteCommandAsync(cancellationToken);

    /// <summary>
    /// 把仍标记为媒体待归档的消息改为内容缺失。只在本进程开始归档之前调用：这些是上次进程停止时未完成的归档，
    /// 临时链接通常已失效，不再重试。
    /// </summary>
    /// <returns>更新的消息数。</returns>
    public async Task<int> MarkInterruptedAsync(long selfId, CancellationToken cancellationToken)
    {
        // 状态只有两个位，清除待归档并置缺失后两种待归档状态都变为缺失；条件与部分索引一致。
        short pendingOnly        = ChatMessageTable.PENDING_ONLY;
        short pendingWithMissing = ChatMessageTable.PENDING_WITH_MISSING;
        short missing            = (short)ChatArchiveFlags.ContentMissing;
        return await _database.Account(selfId)
                              .Updateable<ChatMessageRecord>()
                              .SetColumns(row => new ChatMessageRecord { ArchiveFlags = missing })
                              .Where(row => row.ArchiveFlags == pendingOnly || row.ArchiveFlags == pendingWithMissing)
                              .ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>写入已结束的归档缺口。</summary>
    public async Task WriteGapsAsync(long selfId, IReadOnlyList<ArchiveGap> gaps, CancellationToken cancellationToken)
    {
        if (gaps.Count == 0) return;

        List<ArchiveGapRecord> records =
        [
            .. gaps.Select(gap => new ArchiveGapRecord
            {
                Cause     = (short)gap.Cause,
                StartedAt = gap.StartedAt,
                EndedAt   = gap.EndedAt
            })
        ];
        await _database.Account(selfId).Insertable(records).ExecuteCommandAsync(cancellationToken);
    }

    /// <summary>删除统计日期早于 <paramref name="cutoff" /> 的发言统计。</summary>
    /// <returns>删除的行数。</returns>
    public async Task<int> DeleteStatsBeforeAsync(long selfId, DateOnly cutoff, CancellationToken cancellationToken) =>
        await _database.Account(selfId)
                       .Deleteable<ChatStatDailyRecord>()
                       .Where(stat => stat.StatDate < cutoff)
                       .ExecuteCommandAsync(cancellationToken);

    private static async Task CountAsync(SqlSugarScope db, ArchivedChatMessage message)
    {
        ChatMessageCounts counts = message.Counts;
        DateOnly statDate = message.StatDate;
        short type = (short)message.ConversationType;
        int updated = await db.Updateable<ChatStatDailyRecord>()
                              .SetColumns(stat => new ChatStatDailyRecord
                              {
                                  MessageCount = stat.MessageCount + 1,
                                  CharCount    = stat.CharCount + counts.CharCount,
                                  ImageCount   = stat.ImageCount + counts.ImageCount,
                                  AudioCount   = stat.AudioCount + counts.AudioCount,
                                  VideoCount   = stat.VideoCount + counts.VideoCount,
                                  FileCount    = stat.FileCount + counts.FileCount
                              })
                              .Where(stat => stat.StatDate == statDate
                                             && stat.ConversationType == type
                                             && stat.ConversationId == message.ConversationId
                                             && stat.SenderId == message.SenderId)
                              .ExecuteCommandAsync();
        if (updated > 0) return;

        await db.Insertable(
                    new ChatStatDailyRecord
                    {
                        StatDate         = statDate,
                        ConversationType = type,
                        ConversationId   = message.ConversationId,
                        SenderId         = message.SenderId,
                        MessageCount     = 1,
                        CharCount        = counts.CharCount,
                        ImageCount       = counts.ImageCount,
                        AudioCount       = counts.AudioCount,
                        VideoCount       = counts.VideoCount,
                        FileCount        = counts.FileCount
                    })
                .ExecuteCommandAsync();
    }
}
