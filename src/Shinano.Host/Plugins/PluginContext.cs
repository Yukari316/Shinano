using Shinano.Framework.Plugins;

namespace Shinano.Host.Plugins;

/// <summary>宿主为一个插件提供的能力集合。</summary>
internal sealed class PluginContext : IPluginInitializationContext
{
    /// <summary>机密文件所在目录名，位于部署数据目录下。</summary>
    public const string SECRETS_DIRECTORY_NAME = "secrets";

    private readonly PluginConfigStore    _configStore;
    private readonly string               _secretsPath;
    private readonly Lock                 _lock = new();
    private          IPluginConfigHolder? _config;
    private          object?              _secrets;

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
        string            dataDirectory)
    {
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
        _secretsPath  = Path.Combine(dataDirectory, SECRETS_DIRECTORY_NAME, pluginId + ".json");
    }

    /// <inheritdoc />
    public string PluginId { get; }

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

    /// <inheritdoc />
    public IPluginSecrets<T> Secrets<T>() where T : class, new()
    {
        lock (_lock)
        {
            _secrets ??= new PluginSecrets<T>(_secretsPath);
            return _secrets as IPluginSecrets<T>
                   ?? throw new InvalidOperationException($"插件 {PluginId} 已使用其他机密类型");
        }
    }
}
