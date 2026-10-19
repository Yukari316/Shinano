using SqlSugar;

namespace Shinano.Infrastructure.Database;

/// <summary>
/// 每个 schema 内的结构版本表，按所有者（宿主或插件 ID）记录已执行的显式迁移版本。
/// 版本记录与迁移在同一 schema、同一事务中提交。
/// </summary>
[SugarTable("schema_version")]
public sealed class SchemaVersionRecord
{
    /// <summary>所有者：宿主为 <see cref="SchemaMigrator.HOST_OWNER" />，插件为插件 ID。</summary>
    [SugarColumn(ColumnName = "owner", IsPrimaryKey = true, Length = 128)]
    public string Owner { get; set; } = string.Empty;

    /// <summary>已执行的最高迁移版本。</summary>
    [SugarColumn(ColumnName = "version")]
    public int Version { get; set; }

    /// <summary>最近一次执行迁移的时间。</summary>
    [SugarColumn(ColumnName = "applied_at")]
    public DateTimeOffset AppliedAt { get; set; }
}
