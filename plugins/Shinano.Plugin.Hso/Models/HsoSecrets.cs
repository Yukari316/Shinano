using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.Hso.Models;

/// <summary>Hso 插件机密。</summary>
public sealed class HsoSecrets
{
    /// <summary>图源服务访问令牌。</summary>
    [Display(Name = "图源服务令牌", Description = "YukariApiNext 的访问令牌，需要 Pixiv 转发与搜索转发权限")]
    public string? GatewayToken { get; set; }
}
