using Autofac;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Entertainment.Models;

namespace Shinano.Plugin.Entertainment;

/// <summary>娱乐插件：骰子、随机选择与娱乐禁言，没有存储与后台任务。</summary>
public sealed class EntertainmentPlugin : IShinanoPlugin
{
    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel) => panel.UseConfig<EntertainmentConfig>();

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
