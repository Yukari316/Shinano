using SqlSugar;

namespace Shinano.Plugin.Bilibili.Persistence;

/// <summary>
/// 当前部署的 B站账号，位于全局 schema，最多一行：登录成功时写入，主动退出（换号）时删除。登录失效后保留，
/// 用于在进程重启后仍能识别失效、只通知一次，并继续按该账号的关注列表推送直播。
/// </summary>
[SugarTable("bilibili_account")]
public sealed class BiliAccountRecord
{
    /// <summary>B站账号 UID。</summary>
    [SugarColumn(ColumnName = "uid", IsPrimaryKey = true)]
    public long Uid { get; set; }

    /// <summary>昵称。</summary>
    [SugarColumn(ColumnName = "name")]
    public string Name { get; set; } = "";

    /// <summary>是否已登录失效并已通知 Super User。</summary>
    [SugarColumn(ColumnName = "expired")]
    public bool Expired { get; set; }
}
