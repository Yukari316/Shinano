using Shinano.Framework.Plugins;
using Shinano.Host.Tools;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>按插件 ID 使用宿主浏览器服务。</summary>
internal sealed class PluginBrowser : IPluginBrowser
{
    private readonly string         _pluginId;
    private readonly BrowserService _service;

    /// <summary>创建插件的浏览器入口。</summary>
    public PluginBrowser(string pluginId, BrowserService service)
    {
        _pluginId = pluginId;
        _service  = service;
    }

    /// <inheritdoc />
    public Task<IBrowserSession> OpenAsync(CancellationToken cancellationToken) => _service.OpenAsync(_pluginId, cancellationToken);

    /// <inheritdoc />
    public Task ClearProfileAsync(CancellationToken cancellationToken) => _service.ClearProfileAsync(_pluginId, cancellationToken);
}
