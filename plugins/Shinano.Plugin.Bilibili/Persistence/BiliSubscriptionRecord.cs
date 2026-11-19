using SqlSugar;

namespace Shinano.Plugin.Bilibili.Persistence;

/// <summary>一个 UP 的推送设置，位于机器人账号 schema；推送的群见 <see cref="BiliTargetRecord" />。没有记录的 UP 不推送。</summary>
[SugarTable("bilibili_subscription")]
public sealed class BiliSubscriptionRecord
{
    /// <summary>UP 的 UID。</summary>
    [SugarColumn(ColumnName = "uid", IsPrimaryKey = true)]
    public long Uid { get; set; }

    /// <summary>是否推送动态。</summary>
    [SugarColumn(ColumnName = "push_dynamic")]
    public bool PushDynamic { get; set; }

    /// <summary>是否推送开播与下播。</summary>
    [SugarColumn(ColumnName = "push_live")]
    public bool PushLive { get; set; }
}
