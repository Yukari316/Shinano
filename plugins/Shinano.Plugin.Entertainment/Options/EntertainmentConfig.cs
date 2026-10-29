using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Entertainment.Options;

/// <summary>娱乐插件配置。</summary>
public sealed class EntertainmentConfig
{
    /// <summary>QQ 允许的最长禁言时间（30 天），单位分钟。</summary>
    public const int MAX_MUTE_MINUTES = 43200;

    /// <summary>娱乐禁言的时长，单位分钟，默认 480（8 小时）。</summary>
    [Display(Name = "禁言时长（分钟）", Description = "发送优质睡眠等指令后禁言发送者的时长，最长 30 天")]
    [Range(1, MAX_MUTE_MINUTES)]
    public int MuteMinutes { get; set; } = 480;

    /// <summary>随机选择时回答"我全都要"的概率，单位百分比，默认 10。</summary>
    [Display(Name = "「我全都要」概率（%）", Description = "随机选择时不选任何一项、回答我全都要的概率")]
    [Range(0, 100)]
    public int TakeAllPercent { get; set; } = 10;
}
