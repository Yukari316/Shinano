using Npgsql;
using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Database;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>
/// 账号 schema 中的聊天消息表。按 <c>sent_at</c> 的自然日 RANGE 分区，分区由
/// <see cref="ChatPartitionMaintainer" /> 以显式 SQL 维护，不交给 SqlSugar CodeFirst。
/// </summary>
public static class ChatMessageTable
{
    /// <summary>分区父表名。</summary>
    public const string TABLE_NAME = "chat_message";

    /// <summary>媒体待归档且尚无缺失的状态值。</summary>
    public const short PENDING_ONLY = (short)ChatArchiveFlags.MediaPending;

    /// <summary>媒体待归档且已有缺失的状态值。</summary>
    public const short PENDING_WITH_MISSING = (short)(ChatArchiveFlags.MediaPending | ChatArchiveFlags.ContentMissing);

    /// <summary>
    /// 创建不存在的父表与索引。段 JSON 列使用 lz4 压缩，压缩方式与索引随父表继承到之后创建的分区。
    /// 部分索引只覆盖媒体待归档的行（状态值为待归档，或待归档且已有缺失），
    /// 供启动时把上次进程未完成的归档标记为缺失。
    /// </summary>
    public static async Task EnsureAsync(ShinanoDatabase database, long selfId, CancellationToken cancellationToken)
    {
        string schema = SchemaNames.RequireIdentifier(database.Schemas.Account(selfId));
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
        // SQL_NEED: SqlSugar CodeFirst 不支持分区表、列压缩方式与部分索引。
        await using NpgsqlCommand command = new(
            $"""
             create table if not exists {schema}.{TABLE_NAME} (
                 id                bigint generated always as identity,
                 sent_at           timestamptz not null,
                 conversation_type smallint    not null,
                 conversation_id   bigint      not null,
                 sender_id         bigint      not null,
                 message_id        bigint      not null,
                 text              text,
                 segments          jsonb compression lz4,
                 archive_flags     smallint    not null default 0,
                 primary key (id, sent_at),
                 unique (conversation_type, conversation_id, message_id, sent_at)
             ) partition by range (sent_at);
             create index if not exists ix_{TABLE_NAME}_archive_pending
                 on {schema}.{TABLE_NAME} (sent_at)
                 where archive_flags in ({PENDING_ONLY}, {PENDING_WITH_MISSING})
             """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
