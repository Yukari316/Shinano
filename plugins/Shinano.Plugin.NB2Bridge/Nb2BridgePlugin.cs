using Autofac;
using Shinano.Framework.Plugins;
using Shinano.Plugin.NB2Bridge.Milky;
using Shinano.Plugin.NB2Bridge.Panel;
using Shinano.Plugin.NB2Bridge.PluginPackages;
using Shinano.Plugin.NB2Bridge.Runtime;
using Shinano.Plugin.NB2Bridge.Scope;
using Sora.Entities.Events;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>
/// NB2Bridge：托管 NoneBot2 服务进程（随插件启停），扮演 Milky 实现端在宿主事件与 NB2 之间转换，NB2 的发送经 Sora 执行并归档；
/// 专有页面管理进程、响应范围、NB2 插件（安装、卸载、启停、配置）与日志，Super User 可私聊或群聊「NB2状态」。
/// </summary>
public sealed class Nb2BridgePlugin : IShinanoPlugin
{
    private const string PAGE_TITLE  = "NB2";
    private const string PAGE_ENTRY  = "index.js";
    private const string CLEANUP_JOB = "清理 NB2 发送的临时文件";

    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);

    private ILifetimeScope? _container;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.RegisterInstance(TimeProvider.System);
        builder.Register(scope => new Nb2Layout(scope.Resolve<IPluginInitializationContext>().DataDirectory)).SingleInstance();
        builder.RegisterType<Nb2Catalog>().SingleInstance();
        builder.RegisterType<EventTracker>().SingleInstance();
        builder.RegisterType<Nb2ScopeStore>().SingleInstance();
        builder.RegisterType<LocalFiles>().SingleInstance();
        builder.RegisterType<MilkyActions>().SingleInstance();
        builder.RegisterType<Nb2Runner>().SingleInstance();
        builder.RegisterType<Nb2EventRelay>().SingleInstance();
        builder.RegisterType<Nb2Packages>().SingleInstance();
        builder.RegisterType<Nb2PanelApi>().SingleInstance();
        // 初始化时从同一个容器取得服务，与指令实例共用同一批单例。
        builder.RegisterBuildCallback(scope => _container = scope);
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel) => panel.UseConfig<Nb2Config>();

    /// <inheritdoc />
    public async ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        ILifetimeScope container = _container ?? throw new InvalidOperationException("插件容器尚未建立");
        await container.Resolve<Nb2ScopeStore>().LoadAsync();
        Nb2EventRelay relay = container.Resolve<Nb2EventRelay>();
        context.Events.Subscribe<BotEvent>(relay.HandleAsync);
        context.Scheduler.Register(CLEANUP_JOB, CleanupInterval, container.Resolve<LocalFiles>().CleanupAsync, runImmediately: true);
        context.OnEnabledChanged(container.Resolve<Nb2Runner>().OnEnabledChanged);
        context.Panel.UsePage(PAGE_TITLE, PAGE_ENTRY);
        context.Panel.MapApi(container.Resolve<Nb2PanelApi>().Map);
    }
}
