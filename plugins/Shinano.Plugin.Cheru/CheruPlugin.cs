using Autofac;
using Shinano.Framework.Plugins;

namespace Shinano.Plugin.Cheru;

/// <summary>切噜插件：文本与切噜语互相转换，没有可配置项、存储与后台任务。</summary>
public sealed class CheruPlugin : IShinanoPlugin
{
    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel)
    {
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
