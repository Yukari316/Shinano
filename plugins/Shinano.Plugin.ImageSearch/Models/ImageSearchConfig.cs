using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.ImageSearch.Models;

/// <summary>搜图插件配置。图源服务令牌在插件机密中。</summary>
public sealed class ImageSearchConfig
{
    /// <summary>图源服务（YukariApiNext）地址。</summary>
    [Display(Name = "图源服务地址", Description = "YukariApiNext 网关地址，搜索与原图都经它转发；访问令牌在下方机密中设置", Order = 1)]
    [Required]
    public string GatewayUrl { get; set; } = "https://api.kosaka-wakamo.top/";

    /// <summary>发送指令后等待图片的时间，单位秒。</summary>
    [Display(Name = "等待发图时间（秒）", Description = "发送「搜图」「搜番」后没有附图时，等待发起人发图的时间", Order = 2)]
    [Range(10, 600)]
    public int WaitSeconds { get; set; } = 60;

    /// <summary>SauceNAO 相似度阈值，单位百分比。</summary>
    [Display(Name = "SauceNAO 相似度阈值（%）", Description = "低于阈值时仍回复相似度最高的结果，卡片为警告色；SauceNAO 不可用时改查 IQDB", Order = 3)]
    [Range(0, 100)]
    public int SauceNaoThreshold { get; set; } = 60;

    /// <summary>IQDB 相似度阈值，单位百分比。</summary>
    [Display(Name = "IQDB 相似度阈值（%）", Description = "SauceNAO 不可用、改查 IQDB 时使用；低于阈值时卡片为警告色", Order = 4)]
    [Range(0, 100)]
    public int IqdbThreshold { get; set; } = 80;

    /// <summary>搜番相似度阈值，单位百分比。</summary>
    [Display(Name = "搜番相似度阈值（%）", Description = "trace.moe 低于阈值时卡片为警告色；trace.moe 不可用时改查 SauceNAO 的番剧库", Order = 5)]
    [Range(0, 100)]
    public int AnimeThreshold { get; set; } = 90;

    /// <summary>原图大小上限，单位 MiB。</summary>
    [Display(Name = "原图大小上限（MiB）", Description = "超过时不发送原图；实际上限不超过部署的单文件上限", Order = 6)]
    [Range(1, 1024)]
    public int MaxOriginalMiB { get; set; } = 100;

    /// <summary>插画卡片最多显示的标签数。</summary>
    [Display(Name = "最多显示标签数", Description = "插画卡片的 TAG 行最多显示的标签个数，按来源作品中的顺序截取；为 0 时不显示 TAG 行", Order = 7)]
    [Range(0, 100)]
    public int MaxTags { get; set; } = 20;
}
