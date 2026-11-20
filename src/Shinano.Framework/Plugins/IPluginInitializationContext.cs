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

    /// <summary>宿主提供的公共字体与文字绘制辅助。</summary>
    IPluginFonts Fonts { get; }

    /// <summary>宿主统一风格的图片绘制（文本长图）。</summary>
    IPluginImages Images { get; }

    /// <summary>宿主浏览器服务，使用插件自己的持久用户目录。</summary>
    IPluginBrowser Browser { get; }

    /// <summary>部署数据目录（绝对路径）。只供管理外部程序与其数据的插件（如 NB2Bridge 的 NB2 运行环境）使用，插件只使用约定给自己的子目录。</summary>
    string DataDirectory { get; }

    /// <summary>宿主提供的外部程序（ffmpeg）。</summary>
    IPluginTools Tools { get; }

    /// <summary>
    /// 部署配置的时区（<c>Deployment:TimeZone</c>，与聊天分区、发言统计相同）。按日计数、在卡片中显示时间等与日期有关的业务都使用它，
    /// 不使用机器本地时区；有固定时区语义的数据（如 B站动态按北京时间显示）除外。修改后重启生效。
    /// </summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>
    /// 登记插件启停回调：插件被启用（含首次初始化完成后）时以 true 调用，被管理员停用时以 false 调用；数据库离线时的暂停不触发。
    /// 只供必须随启停管理外部进程的插件使用（如 NB2Bridge）。回调在宿主启停流程中同步执行，须很快返回，耗时工作放到后台；
    /// 回调抛出的异常只记录日志。只能在 <see cref="IShinanoPlugin.InitializeAsync" /> 中登记。
    /// </summary>
    void OnEnabledChanged(Action<bool> handler);

    /// <summary>经宿主通知全部 Super User。</summary>
    IPluginNotices Notices { get; }

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
