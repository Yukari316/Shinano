using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Hso;

/// <summary>Hso 插件配置。图源服务令牌在插件机密中，与搜图插件分开。</summary>
public sealed class HsoConfig
{
    /// <summary>图源服务（YukariApiNext）地址。</summary>
    [Display(Name = "图源服务地址", Description = "YukariApiNext 网关地址，用于 AD 导入与补全作品信息；访问令牌在下方机密中设置", Order = 1)]
    [Required]
    public string GatewayUrl { get; set; } = "https://api.kosaka-wakamo.top/";

    /// <summary>没有 pid 的图片用 SauceNAO 补全作品信息时的相似度阈值，单位百分比。</summary>
    [Display(Name = "SauceNAO 相似度阈值（%）", Description = "没有 pid 的图片补全作品信息时用 SauceNAO 以图搜图，低于阈值视为没有找到，之后不再查询", Order = 2)]
    [Range(0, 100)]
    public int SauceNaoThreshold { get; set; } = 80;

    /// <summary>不允许 NSFW 的群抽到作品信息尚未补全的图片时，是否照常发送。</summary>
    [Display(
        Name = "补全缺失的图片信息时是否发送 NSFW 图片",
        Description = "只影响不允许 NSFW 的群：关闭时，抽到作品信息尚未补全（无法确定是否 NSFW）的图片不发送，改为后台补全它并改抽一张已确定不是 NSFW 的图；开启时照常发送，可能发出 NSFW 图片",
        Order = 3)]
    public bool SendPendingInRestrictedGroups { get; set; }

    /// <summary>后台任务每天自动补全的图片数。</summary>
    [Display(
        Name = "每天自动补全的图片数",
        Description = "后台任务每天补全作品信息的待补全图片数，避免不允许 NSFW 的群长期抽不到新导入的图片；为 0 时不自动补全。没有 pid 的图片每张用一次 SauceNAO 额度",
        Order = 4)]
    [Range(0, 100)]
    public int DailyCompletions { get; set; } = 5;
}
