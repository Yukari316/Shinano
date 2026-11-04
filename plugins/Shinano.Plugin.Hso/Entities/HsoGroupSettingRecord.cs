using SqlSugar;

namespace Shinano.Plugin.Hso.Entities;

/// <summary>群设置，位于机器人账号 schema；没有记录的群不屏蔽、不允许 NSFW。</summary>
[SugarTable("hso_group_setting")]
public sealed class HsoGroupSettingRecord
{
    /// <summary>群号。</summary>
    [SugarColumn(ColumnName = "group_id", IsPrimaryKey = true)]
    public long GroupId { get; set; }

    /// <summary>是否屏蔽：屏蔽的群中全部 Hso 指令不响应。</summary>
    [SugarColumn(ColumnName = "blocked")]
    public bool Blocked { get; set; }

    /// <summary>是否允许 NSFW 图片；不允许时只抽取非 NSFW 图片。</summary>
    [SugarColumn(ColumnName = "allow_nsfw")]
    public bool AllowNsfw { get; set; }
}
