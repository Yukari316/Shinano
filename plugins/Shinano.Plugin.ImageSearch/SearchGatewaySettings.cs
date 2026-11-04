using Shinano.Framework.Plugins;
using Shinano.Plugin.ImageSearch.Models;
using Shinano.Plugin.ImageSources;

namespace Shinano.Plugin.ImageSearch;

/// <summary>搜图插件的图源服务地址取插件配置，令牌取插件机密，与 Hso 的分开配置。</summary>
public sealed class SearchGatewaySettings : IGatewaySettings
{
    private readonly IPluginInitializationContext _context;

    /// <summary>创建设置。</summary>
    public SearchGatewaySettings(IPluginInitializationContext context) => _context = context;

    /// <inheritdoc />
    public string BaseUrl => _context.Config<ImageSearchConfig>().Value.GatewayUrl;

    /// <inheritdoc />
    public string? Token => _context.Secrets<ImageSearchSecrets>().Value.GatewayToken;
}
