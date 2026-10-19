using Shinano.Framework.Plugins;

namespace Shinano.Host.Plugins;

/// <summary>宿主为一个插件提供的能力集合。</summary>
internal sealed class PluginContext : IPluginInitializationContext
{
    /// <summary>机密文件所在目录名，位于部署数据目录下。</summary>
    public const string SECRETS_DIRECTORY_NAME = "secrets";

    private readonly PluginConfigStore _configStore;
    private readonly string            _secretsPath;
    private readonly Lock              _lock = new();
    private          object?           _config;
    private          object?           _secrets;

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

    /// <inheritdoc />
    public IPluginConfig<T> Config<T>() where T : class, new()
    {
        lock (_lock)
        {
            _config ??= new PluginConfig<T>(_configStore.Load<T>(PluginId));
            return _config as IPluginConfig<T>
                   ?? throw new InvalidOperationException($"插件 {PluginId} 已使用其他配置类型");
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
