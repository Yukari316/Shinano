using Autofac;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili.Feed;
using Shinano.Plugin.Bilibili.Login;
using Shinano.Plugin.Bilibili.Panel;
using Shinano.Plugin.Bilibili.Persistence;

namespace Shinano.Plugin.Bilibili;

/// <summary>
/// B站订阅插件：推送登录账号关注的 UP 的动态（宿主风格的卡片，经合并转发）与开播、下播提醒。登录由宿主浏览器维护，
/// 平时以普通 HTTP 读取关注流；面板页负责扫码登录与按 UP 设置推送，Super User 也可私聊「B站登录」。
/// </summary>
public sealed class BilibiliPlugin : IShinanoPlugin
{
    private const string PAGE_TITLE      = "B站订阅";
    private const string PAGE_ENTRY      = "index.js";
    private const string POLL_JOB        = "轮询 B站动态与直播";
    private const string FOLLOWS_JOB     = "刷新 B站关注列表";
    private const string MAINTENANCE_JOB = "维护 B站浏览器登录";

    private ILifetimeScope? _container;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.RegisterInstance(TimeProvider.System);
        builder.RegisterType<BiliHttp>().SingleInstance();
        builder.RegisterType<BiliSession>().SingleInstance();
        builder.RegisterType<BiliLogin>().SingleInstance();
        builder.RegisterType<BiliStore>().SingleInstance();
        builder.RegisterType<BiliCardRenderer>().SingleInstance();
        builder.RegisterType<BiliPusher>().SingleInstance();
        builder.RegisterType<BiliPoller>().SingleInstance();
        builder.RegisterType<BiliPanelApi>().SingleInstance();
        // 初始化时从同一个容器取得服务，与指令实例共用同一批单例。
        builder.RegisterBuildCallback(scope => _container = scope);
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel) => panel.UseConfig<BiliConfig>();

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        ILifetimeScope container = _container ?? throw new InvalidOperationException("插件容器尚未建立");
        container.Resolve<BiliStore>().Initialize();
        BiliPoller poller = container.Resolve<BiliPoller>();
        // 启用时先维护一次：导出浏览器中的登录状态，并刷新关注列表。
        context.Scheduler.Register(MAINTENANCE_JOB, BiliPoller.MaintenanceInterval, poller.MaintainAsync, runImmediately: true);
        context.Scheduler.Register(FOLLOWS_JOB, BiliPoller.FollowsInterval, async token => await poller.RefreshFollowsAsync(token), runImmediately: false);
        context.Scheduler.Register(POLL_JOB, BiliPoller.TickInterval, poller.PollAsync, runImmediately: false);
        context.Panel.UsePage(PAGE_TITLE, PAGE_ENTRY);
        context.Panel.MapApi(container.Resolve<BiliPanelApi>().Map);
        return ValueTask.CompletedTask;
    }
}
