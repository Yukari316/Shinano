using Autofac;

namespace Shinano.Framework.Plugins;

/// <summary>
/// 插件入口。宿主加载插件程序集后实例化一次实现类（需要公开无参构造函数），方法由宿主串行调用。
/// 插件不实现停用方法：停用由宿主统一完成，停用后指令、事件订阅与后台任务都不再被触发。
/// </summary>
public interface IShinanoPlugin
{
    /// <summary>
    /// 向插件容器注册服务，只在加载时调用一次。带 <c>[CommandGroup]</c> 的公开类型由宿主自动注册为单例，
    /// 其构造函数可以注入此处注册的服务与 <see cref="IPluginInitializationContext" />。
    /// </summary>
    void ConfigureServices(ContainerBuilder builder);

    /// <summary>
    /// 声明管理面板的配置表单，只在加载时调用一次，与启停无关：插件加入后即可在面板显示和修改配置。
    /// 专有面板（前端页面与管理 API）另在 <see cref="InitializeAsync" /> 中经
    /// <see cref="IPluginInitializationContext.Panel" /> 登记。
    /// </summary>
    void ConfigurePanel(IPanelRegistration panel);

    /// <summary>
    /// 首次启用时调用一次：执行结构迁移、登记事件订阅与后台任务。抛出异常时插件保持停用并记录原因，
    /// 直到进程重启后才会再次尝试；之后的停用与重新启用不再调用本方法。
    /// </summary>
    ValueTask InitializeAsync(IPluginInitializationContext context, CancellationToken cancellationToken);
}
