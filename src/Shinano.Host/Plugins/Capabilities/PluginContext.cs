using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Host.Plugins.Config;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>宿主为一个插件提供的能力集合。</summary>
internal sealed class PluginContext : IPluginInitializationContext
{
    /// <summary>机密文件所在目录名，位于部署数据目录下。</summary>
    public const string SECRETS_DIRECTORY_NAME = "secrets";

    private readonly PluginConfigStore    _configStore;
    private readonly string               _secretsPath;
    private readonly Lock                 _lock = new();
    private          IPluginConfigHolder?  _config;
    private          IPluginSecretsHolder? _secrets;
    private readonly List<Action<bool>>    _enabledHandlers = [];

    /// <summary>创建能力集合。</summary>
    public PluginContext(
        string            pluginId,
        PluginEvents      events,
        PluginScheduler   scheduler,
        IPluginDatabase   database,
        IPluginMedia      media,
        IPluginBuckets    buckets,
        IBotAccessor      bot,
        IPluginAssets     assets,
        PluginConfigStore configStore,
        PluginPanel       panel,
        IPluginFonts      fonts,
        IPluginImages     images,
        IPluginBrowser    browser,
        IPluginTools      tools,
        IPluginNotices    notices,
        string            dataDirectory,
        TimeZoneInfo      timeZone)
    {
        TimeZone      = timeZone;
        PluginId      = pluginId;
        EventsHost    = events;
        SchedulerHost = scheduler;
        Database      = database;
        Media         = media;
        Buckets       = buckets;
        Bot           = bot;
        Assets        = assets;
        _configStore  = configStore;
        PanelHost     = panel;
        Fonts         = fonts;
        Images        = images;
        Browser       = browser;
        Tools         = tools;
        DataDirectory = dataDirectory;
        Notices       = notices;
        _secretsPath  = Path.Combine(dataDirectory, SECRETS_DIRECTORY_NAME, pluginId + ".json");
    }

    /// <inheritdoc />
    public string PluginId { get; }

    /// <inheritdoc />
    public TimeZoneInfo TimeZone { get; }

    /// <summary>宿主侧的事件订阅集合。</summary>
    public PluginEvents EventsHost { get; }

    /// <summary>宿主侧的后台任务登记。</summary>
    public PluginScheduler SchedulerHost { get; }

    /// <inheritdoc />
    public IPluginEvents Events => EventsHost;

    /// <inheritdoc />
    public IPluginScheduler Scheduler => SchedulerHost;

    /// <inheritdoc />
    public IPluginDatabase Database { get; }

    /// <inheritdoc />
    public IPluginMedia Media { get; }

    /// <inheritdoc />
    public IPluginBuckets Buckets { get; }

    /// <inheritdoc />
    public IBotAccessor Bot { get; }

    /// <inheritdoc />
    public IPluginAssets Assets { get; }

    /// <summary>宿主侧的专有面板。</summary>
    public PluginPanel PanelHost { get; }

    /// <inheritdoc />
    public IPluginPanel Panel => PanelHost;

    /// <inheritdoc />
    public IPluginFonts Fonts { get; }

    /// <inheritdoc />
    public IPluginImages Images { get; }

    /// <inheritdoc />
    public IPluginBrowser Browser { get; }

    /// <inheritdoc />
    public IPluginTools Tools { get; }

    /// <inheritdoc />
    public string DataDirectory { get; }

    /// <inheritdoc />
    public void OnEnabledChanged(Action<bool> handler) => _enabledHandlers.Add(handler);

    /// <summary>清空上次初始化失败时留下的事件订阅、后台任务、页面与 API、启停回调，重试初始化前调用。</summary>
    public void ResetInitialization()
    {
        EventsHost.Reset();
        SchedulerHost.Reset();
        PanelHost.Reset();
        _enabledHandlers.Clear();
    }

    /// <summary>依次调用登记的启停回调；回调异常只记录日志。</summary>
    public void NotifyEnabled(bool enabled, ILogger logger)
    {
        foreach (Action<bool> handler in _enabledHandlers)
        {
            try
            {
                handler(enabled);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "插件 {PluginId} 的启停回调失败", PluginId);
            }
        }
    }

    /// <inheritdoc />
    public IPluginNotices Notices { get; }

    /// <summary>插件在加载时声明的配置类型；未声明时为 null。</summary>
    public Type? ConfigType { get; set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException"><typeparamref name="T" /> 不是插件声明的配置类型。</exception>
    public IPluginConfig<T> Config<T>() where T : class, new()
    {
        if (ConfigType != typeof(T))
            throw new InvalidOperationException($"插件 {PluginId} 未在 ConfigurePanel 中声明配置类型 {typeof(T).Name}");

        lock (_lock)
        {
            _config ??= new PluginConfig<T>((T)_configStore.Load(PluginId, typeof(T)));
            // SAFE: 配置类型固定为 ConfigType，_config 只会是 PluginConfig<T>。
            return (IPluginConfig<T>)_config;
        }
    }

    /// <summary>读取当前配置：插件已取用配置时返回其当前值，否则从数据库读取。</summary>
    public object LoadConfig(Type configType)
    {
        lock (_lock)
        {
            return _config?.Value ?? _configStore.Load(PluginId, configType);
        }
    }

    /// <summary>保存配置，并让插件下次读取 <see cref="IPluginConfig{T}.Value" /> 时得到新值。</summary>
    public async Task SaveConfigAsync(Type configType, object config, CancellationToken cancellationToken)
    {
        await _configStore.SaveAsync(PluginId, configType, config, cancellationToken);
        lock (_lock)
        {
            _config?.Replace(config);
        }
    }

    /// <summary>插件在 ConfigurePanel 中声明的机密类型；未声明时为 null。</summary>
    public Type? SecretsType { get; set; }

    /// <inheritdoc />
    public IPluginSecrets<T> Secrets<T>() where T : class, new()
    {
        if (SecretsType is not null && SecretsType != typeof(T))
            throw new InvalidOperationException($"插件 {PluginId} 在 ConfigurePanel 中声明的机密类型是 {SecretsType.Name}，不是 {typeof(T).Name}");

        lock (_lock)
        {
            _secrets ??= new PluginSecrets<T>(_secretsPath);
            return _secrets as IPluginSecrets<T>
                   ?? throw new InvalidOperationException($"插件 {PluginId} 已使用其他机密类型");
        }
    }

    /// <summary>按声明的机密类型取得机密，与插件经 <see cref="Secrets{T}" /> 取得的是同一实例。</summary>
    /// <exception cref="System.Text.Json.JsonException">机密文件内容与机密类型不兼容。</exception>
    public IPluginSecretsHolder SecretsHolder(Type secretsType)
    {
        lock (_lock)
        {
            try
            {
                // SAFE: PluginSecrets<T> 有接受文件路径的公共构造函数，且实现 IPluginSecretsHolder。
                _secrets ??= (IPluginSecretsHolder)Activator.CreateInstance(typeof(PluginSecrets<>).MakeGenericType(secretsType), _secretsPath)!;
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            }

            return _secrets;
        }
    }
}
