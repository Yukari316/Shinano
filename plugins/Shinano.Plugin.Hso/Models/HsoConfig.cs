using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Hso.Models;

/// <summary>Hso 插件配置。图源服务令牌在插件机密中，与搜图插件分开。</summary>
public sealed class HsoConfig
{
    /// <summary>图源服务（YukariApiNext）地址。</summary>
    [Display(Name = "图源服务地址", Description = "YukariApiNext 网关地址，用于 AD 导入与补全作品信息；访问令牌在下方机密中设置", Order = 1)]
    [Required]
    public string GatewayUrl { get; set; } = "https://api.kosaka-wakamo.top/";

    /// <summary>没有 pid 的图片用 SauceNAO 补全作品信息时的相似度阈值，单位百分比。</summary>
    [Display(Name = "SauceNAO 相似度阈值（%）", Description = "没有 pid 的图片被抽中时用 SauceNAO 查作品信息，低于阈值视为没有找到，之后不再查询", Order = 2)]
    [Range(0, 100)]
    public int SauceNaoThreshold { get; set; } = 80;
}
