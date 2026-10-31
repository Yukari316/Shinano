using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Qa.Options;

/// <summary>有人问插件配置。</summary>
public sealed class QaConfig
{
    /// <summary>每次正则匹配的超时，单位毫秒，默认 100；超时视为未命中并告警。</summary>
    [Display(Name = "正则匹配超时（毫秒）", Description = "单个正则词条每次匹配的最长时间，超时视为未命中并记录警告")]
    [Range(10, 5000)]
    public int RegexTimeoutMilliseconds { get; set; } = 100;

    /// <summary>"看看有人问"每页列出的条数，默认 30。</summary>
    [Display(Name = "列表每页条数", Description = "看看有人问的长图每页列出的词条数")]
    [Range(5, 100)]
    public int PageSize { get; set; } = 30;

    /// <summary>覆盖确认的等待时间，单位秒，默认 60。</summary>
    [Display(Name = "覆盖确认等待（秒）", Description = "录入重复问题时等待发起人回复 y 或 n 的时间，超时保留原词条")]
    [Range(10, 600)]
    public int ConfirmTimeoutSeconds { get; set; } = 60;
}
