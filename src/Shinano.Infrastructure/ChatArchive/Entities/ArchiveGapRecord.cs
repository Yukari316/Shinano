using Shinano.Infrastructure.ChatArchive.Models;
using SqlSugar;

namespace Shinano.Infrastructure.ChatArchive.Entities;

/// <summary>账号 schema 中的归档缺口，记录消息无法完整归档的时间区间。</summary>
[SugarTable("archive_gap")]
public sealed class ArchiveGapRecord
{
    /// <summary>行 ID。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>原因，取值见 <see cref="ArchiveGapCause" />。</summary>
    [SugarColumn(ColumnName = "cause")]
    public short Cause { get; set; }

    /// <summary>开始时间，UTC。</summary>
    [SugarColumn(ColumnName = "started_at")]
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>结束时间，UTC。</summary>
    [SugarColumn(ColumnName = "ended_at")]
    public DateTimeOffset EndedAt { get; set; }
}
