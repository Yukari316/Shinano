using SqlSugar;

namespace Shinano.Plugin.Bilibili.Persistence;

/// <summary>一个 UP 推送到的一个群及该群的动态推送进度，位于机器人账号 schema。</summary>
[SugarTable("bilibili_target")]
public sealed class BiliTargetRecord
{
    /// <summary>UP 的 UID。</summary>
    [SugarColumn(ColumnName = "uid", IsPrimaryKey = true)]
    public long Uid { get; set; }

    /// <summary>群号。</summary>
    [SugarColumn(ColumnName = "group_id", IsPrimaryKey = true)]
    public long GroupId { get; set; }

    /// <summary>已推送的最新动态的发布时间（Unix 秒）；还没推送过时为 null，首次只推送最新一条。</summary>
    [SugarColumn(ColumnName = "pushed_at", IsNullable = true)]
    public long? PushedAt { get; set; }

    /// <summary>已推送的最新动态 ID，与 <see cref="PushedAt" /> 一起判断同一秒发布的动态。</summary>
    [SugarColumn(ColumnName = "pushed_id", IsNullable = true)]
    public string? PushedId { get; set; }
}
