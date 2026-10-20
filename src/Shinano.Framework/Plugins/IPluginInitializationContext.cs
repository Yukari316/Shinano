namespace Shinano.Framework.Plugins;

/// <summary>
/// 宿主提供给插件的能力。全部能力按插件归属登记，启停由宿主统一控制。
/// 该对象也注册在插件容器中，可由插件服务的构造函数注入。
/// </summary>
public interface IPluginInitializationContext
{
    /// <summary>插件 ID（<c>plugin.json</c> 中的 <c>id</c>）。</summary>
    string PluginId { get; }

    /// <summary>普通事件订阅。</summary>
    IPluginEvents Events { get; }

    /// <summary>后台任务登记。</summary>
    IPluginScheduler Scheduler { get; }

    /// <summary>数据库访问与结构迁移。</summary>
    IPluginDatabase Database { get; }

    /// <summary>持久媒体（宿主管理，按引用回收）。</summary>
    IPluginMedia Media { get; }

    /// <summary>插件独立 bucket。</summary>
    IPluginBuckets Buckets { get; }

    /// <summary>当前机器人账号的 API，用于没有事件参数的场景（如后台任务）主动发送。</summary>
    IBotAccessor Bot { get; }

    /// <summary>插件包 <c>assets/</c> 目录的只读访问。</summary>
    IPluginAssets Assets { get; }

    /// <summary>专有面板登记：插件启用时出现在管理面板，停用时移除。</summary>
    IPluginPanel Panel { get; }

    /// <summary>
    /// 返回插件配置。<typeparamref name="T" /> 的无参构造结果即默认配置；配置存于数据库，切换账号保留。
    /// 同一插件只能使用一个配置类型，且须与 <see cref="IPanelRegistration.UseConfig{TConfig}" /> 声明的类型相同。
    /// </summary>
    IPluginConfig<T> Config<T>() where T : class, new();

    /// <summary>
    /// 返回插件机密（密钥、登录状态等），只存于部署本地文件，管理面板不读取其值。
    /// 同一插件只能使用一个机密类型。
    /// </summary>
    IPluginSecrets<T> Secrets<T>() where T : class, new();
}
