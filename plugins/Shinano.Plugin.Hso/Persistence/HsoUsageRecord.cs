using SqlSugar;

namespace Shinano.Plugin.Hso.Persistence;

/// <summary>按群与成员的发图次数，位于机器人账号 schema，用于排行。</summary>
[SugarTable("hso_usage")]
public sealed class HsoUsageRecord
{
    /// <summary>群号。</summary>
    [SugarColumn(ColumnName = "group_id", IsPrimaryKey = true)]
    public long GroupId { get; set; }

    /// <summary>成员 QQ 号。</summary>
    [SugarColumn(ColumnName = "user_id", IsPrimaryKey = true)]
    public long UserId { get; set; }

    /// <summary>发图成功的次数。</summary>
    [SugarColumn(ColumnName = "count")]
    public long Count { get; set; }
}
