using Autofac;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Qa.Models;
using Sora.Entities.Events;

namespace Shinano.Plugin.Qa;

/// <summary>有人问插件：群内问答的录入、删除、列表与自动回答，以及管理面板的词条与禁止提问词管理页。</summary>
public sealed class QaPlugin : IShinanoPlugin
{
    private const string PAGE_TITLE = "有人问";
    private const string PAGE_ENTRY = "index.js";

    private ILifetimeScope? _container;

    /// <inheritdoc />
    public void ConfigureServices(ContainerBuilder builder)
    {
        builder.RegisterInstance(TimeProvider.System);
        builder.RegisterType<QaStore>().SingleInstance();
        builder.RegisterType<QaMatcher>().SingleInstance();
        builder.RegisterType<QaMediaStore>().SingleInstance();
        builder.RegisterType<QaAnswerSender>().SingleInstance();
        builder.RegisterType<QaResponder>().SingleInstance();
        builder.RegisterType<QaPanelApi>().SingleInstance();
        // 初始化时从同一个容器取得服务，与指令实例共用同一批单例。
        builder.RegisterBuildCallback(scope => _container = scope);
    }

    /// <inheritdoc />
    public void ConfigurePanel(IPanelRegistration panel) => panel.UseConfig<QaConfig>();

    /// <inheritdoc />
    public ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken)
    {
        ILifetimeScope container = _container ?? throw new InvalidOperationException("插件容器尚未建立");
        container.Resolve<QaStore>().Initialize();
        context.Events.Subscribe<MessageReceivedEvent>(container.Resolve<QaResponder>().OnMessageAsync);
        context.Panel.UsePage(PAGE_TITLE, PAGE_ENTRY);
        context.Panel.MapApi(container.Resolve<QaPanelApi>().Map);
        return ValueTask.CompletedTask;
    }
}
