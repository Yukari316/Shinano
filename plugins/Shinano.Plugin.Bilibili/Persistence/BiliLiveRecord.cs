using SqlSugar;

namespace Shinano.Plugin.Bilibili.Persistence;

/// <summary>一个 UP 上次查询到的直播状态，位于机器人账号 schema，用于判断开播与下播。</summary>
[SugarTable("bilibili_live")]
public sealed class BiliLiveRecord
{
    /// <summary>UP 的 UID。</summary>
    [SugarColumn(ColumnName = "uid", IsPrimaryKey = true)]
    public long Uid { get; set; }

    /// <summary>是否正在直播。</summary>
    [SugarColumn(ColumnName = "live")]
    public bool Live { get; set; }

    /// <summary>本次开播时间；未开播时为 null。</summary>
    [SugarColumn(ColumnName = "live_since", IsNullable = true)]
    public DateTimeOffset? LiveSince { get; set; }
}
