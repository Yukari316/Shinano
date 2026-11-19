using System.ComponentModel.DataAnnotations;

namespace Shinano.Plugin.ImageSearch;

/// <summary>搜图插件机密。</summary>
public sealed class ImageSearchSecrets
{
    /// <summary>图源服务访问令牌。</summary>
    [Display(Name = "图源服务令牌", Description = "YukariApiNext 的访问令牌，需要搜索转发与图站转发权限")]
    public string? GatewayToken { get; set; }
}
