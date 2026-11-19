using SqlSugar;

namespace Shinano.Plugin.Bilibili.Persistence;

/// <summary>
/// B站账号关注的 UP，位于全局 schema，按 B站账号区分：面板只显示当前登录账号的关注，换号后原账号的记录保留、不显示。
/// 以关注列表为准，该账号取关的 UP 被删除。
/// </summary>
[SugarTable("bilibili_follow")]
public sealed class BiliFollowRecord
{
    /// <summary>关注者，即登录的 B站账号 UID。</summary>
    [SugarColumn(ColumnName = "account_uid", IsPrimaryKey = true)]
    public long AccountUid { get; set; }

    /// <summary>UP 的 UID。</summary>
    [SugarColumn(ColumnName = "uid", IsPrimaryKey = true)]
    public long Uid { get; set; }

    /// <summary>昵称。</summary>
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = "";

    /// <summary>头像地址。</summary>
    [SugarColumn(ColumnName = "face")]
    public string Face { get; set; } = "";

    /// <summary>最近一条动态的发布时间；还没见过其动态时为 null。</summary>
    [SugarColumn(ColumnName = "last_dynamic_at", IsNullable = true)]
    public DateTimeOffset? LastDynamicAt { get; set; }
}
