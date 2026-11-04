using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Models;
using Shinano.Plugin.ImageSources;

namespace Shinano.Plugin.Hso;

/// <summary>Hso 插件的图源服务地址取插件配置，令牌取插件机密，与搜图插件的分开配置。</summary>
public sealed class HsoGatewaySettings : IGatewaySettings
{
    private readonly IPluginInitializationContext _context;

    /// <summary>创建设置。</summary>
    public HsoGatewaySettings(IPluginInitializationContext context) => _context = context;

    /// <inheritdoc />
    public string BaseUrl => _context.Config<HsoConfig>().Value.GatewayUrl;

    /// <inheritdoc />
    public string? Token => _context.Secrets<HsoSecrets>().Value.GatewayToken;
}
