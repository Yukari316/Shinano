using Autofac;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Gallery;
using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Panel;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Gateway;

namespace Shinano.Plugin.Hso;

/// <summary>
/// Hso 图库插件：群内随机发送本地图库（插件独立 bucket）中的图片与作品信息卡，按群设置屏蔽与 NSFW，统计发图排行；
/// Super User 以 <c>AD</c> 从 Pixiv 导入；面板页负责上传、群设置、作品信息审核与图库浏览。作品信息经图源服务（YukariApiNext）
/// 在图片被抽中后或由每日任务在后台补全，来源原图与图库图片不一致时进入审核队列。
/// </summary>
public sealed class HsoPlugin : IShinanoPlugin
{
    private const string PAGE_TITLE     = "Hso 图库";
    private const string PAGE_ENTRY     = "index.js";
    private const string CLEANUP_JOB    = "清理 Hso 临时对象";
    private const string COMPLETION_JOB = "每日补全 Hso 作品信息";

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private ILifetimeScope? _container;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.RegisterInstance(TimeProvider.System);
        builder.RegisterType<HsoGatewaySettings>().As<IGatewaySettings>().SingleInstance();
        builder.RegisterType<YukariGateway>().SingleInstance();
        builder.RegisterType<ImageResolver>().SingleInstance();
        builder.RegisterType<HsoGallery>().SingleInstance();
        builder.RegisterType<HsoGroups>().SingleInstance();
        builder.RegisterType<HsoInfoResolver>().SingleInstance();
        builder.RegisterType<HsoOriginals>().SingleInstance();
        builder.RegisterType<HsoReviews>().SingleInstance();
        builder.RegisterType<HsoCompleter>().SingleInstance();
        builder.RegisterType<HsoPixivImporter>().SingleInstance();
        builder.RegisterType<HsoSender>().SingleInstance();
        builder.RegisterType<HsoPanelApi>().SingleInstance();
        // 初始化时从同一个容器取得服务，与指令实例共用同一批单例。
        builder.RegisterBuildCallback(scope => _container = scope);
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel)
    {
        panel.UseConfig<HsoConfig>();
        panel.UseSecrets<HsoSecrets>();
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        ILifetimeScope container = _container ?? throw new InvalidOperationException("插件容器尚未建立");
        await container.Resolve<HsoGallery>().InitializeAsync(cancellationToken);
        container.Resolve<HsoGroups>().Initialize();
        context.Scheduler.Register(CLEANUP_JOB, CleanupInterval, container.Resolve<HsoSender>().CleanupAsync, runImmediately: true);
        // 不在启用时立即执行：重启后当天的计数从 0 开始，频繁重启时会多补全。
        context.Scheduler.Register(COMPLETION_JOB, HsoCompleter.DailyJobInterval, container.Resolve<HsoCompleter>().RunDailyAsync, runImmediately: false);
        context.Panel.UsePage(PAGE_TITLE, PAGE_ENTRY);
        context.Panel.MapApi(container.Resolve<HsoPanelApi>().Map);
    }
}
