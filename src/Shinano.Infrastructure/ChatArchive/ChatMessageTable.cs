using Npgsql;
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

    /// <summary>
    /// 创建不存在的父表。段 JSON 列使用 lz4 压缩，压缩方式随父表继承到之后创建的分区。
    /// </summary>
    public static async Task EnsureAsync(ShinanoDatabase database, long selfId, CancellationToken cancellationToken)
    {
        string schema = SchemaNames.RequireIdentifier(database.Schemas.Account(selfId));
        await using NpgsqlConnection connection = await database.OpenConnectionAsync(cancellationToken);
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
             ) partition by range (sent_at)
             """,
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
