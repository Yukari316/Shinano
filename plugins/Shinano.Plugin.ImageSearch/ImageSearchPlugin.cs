using Autofac;
using Shinano.Framework.Plugins;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Gateway;

namespace Shinano.Plugin.ImageSearch;

/// <summary>
/// 搜图插件：「搜图」以 SauceNAO 为主、IQDB 兜底，「搜番」以 trace.moe 为主、SauceNAO 番剧库兜底；
/// 搜索与原图都经图源服务（YukariApiNext）转发，插件只持有图源服务令牌。
/// </summary>
public sealed class ImageSearchPlugin : IShinanoPlugin
{
    private const string CLEANUP_JOB = "清理搜图临时原图";

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private ILifetimeScope? _container;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.RegisterType<SearchGatewaySettings>().As<IGatewaySettings>().SingleInstance();
        builder.RegisterType<YukariGateway>().SingleInstance();
        builder.RegisterType<ImageResolver>().SingleInstance();
        builder.RegisterType<QueryImageReader>().SingleInstance();
        builder.RegisterType<ResultSender>().SingleInstance();
        // 初始化时从同一个容器取得服务，与指令实例共用同一批单例。
        builder.RegisterBuildCallback(scope => _container = scope);
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel)
    {
        panel.UseConfig<ImageSearchConfig>();
        panel.UseSecrets<ImageSearchSecrets>();
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        ILifetimeScope container = _container ?? throw new InvalidOperationException("插件容器尚未建立");
        context.Scheduler.Register(CLEANUP_JOB, CleanupInterval, container.Resolve<ResultSender>().CleanupAsync, runImmediately: true);
        return ValueTask.CompletedTask;
    }
}
