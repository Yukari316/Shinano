using Autofac;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Host.Bot;
using Shinano.Host.Panel;
using Shinano.Host.Plugins.Capabilities;
using Shinano.Host.Plugins.Config;
using Shinano.Host.Rendering;
using Shinano.Host.Tools;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Sora;
using Sora.Command;
using Sora.Command.Attributes;
using Sora.Entities.Events;
using SqlSugar;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shinano.Host.Plugins;

/// <summary>
/// 原生插件的生命周期与响应控制：发现并加载插件包、首次启用时登记指令并初始化、按插件顺序分发普通事件，
/// 以及启用、停用、排序和数据库离线时的整体暂停。启停与排序从下一条消息生效。
/// </summary>
public sealed class PluginRuntime : IDisposable
{
    private static readonly MethodInfo RegisterInstanceMethod =
        typeof(AssemblyRegistration).GetMethod(nameof(AssemblyRegistration.RegisterCommandInstance))
        ?? throw new MissingMethodException(nameof(AssemblyRegistration), nameof(AssemblyRegistration.RegisterCommandInstance));

    private const string SUSPENDED_ERROR = "数据库离线期间不能修改插件状态";

    private readonly HostInfrastructure     _infrastructure;
    private readonly HostScheduler          _scheduler;
    private readonly BotAccessor            _bot;
    private readonly RuntimeHealth          _health;
    private readonly PluginRuntimeSettings  _settings;
    private readonly PluginEndpointDataSource _endpoints;
    private readonly HostFonts              _fonts;
    private readonly CardRenderer           _images;
    private readonly BrowserService         _browser;
    private readonly FfmpegService          _ffmpeg;
    private readonly SuperUserNotifier      _notices;
    private readonly ILoggerFactory         _loggerFactory;
    private readonly ILogger<PluginRuntime> _logger;
    private readonly SemaphoreSlim          _gate          = new(1, 1);
    private readonly List<LoadedPlugin>     _plugins       = [];
    private          LoadedPlugin[]         _dispatchOrder = [];
    private          SoraService?           _service;
    private volatile bool                   _suspended;
    private          bool                   _accountReady;

    /// <summary>创建运行时。</summary>
    public PluginRuntime(
        HostInfrastructure    infrastructure,
        HostScheduler         scheduler,
        BotAccessor           bot,
        RuntimeHealth         health,
        PluginRuntimeSettings    settings,
        PluginEndpointDataSource endpoints,
        HostFonts                fonts,
        CardRenderer             images,
        BrowserService           browser,
        FfmpegService            ffmpeg,
        SuperUserNotifier        notifier,
        ILoggerFactory           loggerFactory)
    {
        _infrastructure = infrastructure;
        _scheduler      = scheduler;
        _bot            = bot;
        _health         = health;
        _settings       = settings;
        _endpoints      = endpoints;
        _fonts          = fonts;
        _images         = images;
        _browser        = browser;
        _ffmpeg         = ffmpeg;
        _notices        = notifier;
        _loggerFactory  = loggerFactory;
        _logger         = loggerFactory.CreateLogger<PluginRuntime>();
    }

    /// <summary>
    /// 发现插件目录下的插件包，加载程序集并构建插件容器，读取持久化的启用状态与顺序。
    /// 单个插件失败只记录原因，不影响其他插件。只在基础设施就绪后调用一次。
    /// </summary>
    /// <returns>已发现插件的加载结果：插件 ID 到失败原因，成功为 null；清单无效或 ID 重复而跳过的目录不在其中。</returns>
    public async Task<IReadOnlyDictionary<string, string?>> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _infrastructure.Database.Global.CodeFirst.InitTables<PluginStateRecord, PluginConfigRecord>();
            Dictionary<string, PluginStateRecord> states =
                (await _infrastructure.Database.Global.Queryable<PluginStateRecord>().ToListAsync(cancellationToken))
                .ToDictionary(state => state.PluginId);

            foreach (LoadedPlugin plugin in Discover())
            {
                if (plugin.Error is null)
                    Load(plugin);
                if (!states.TryGetValue(plugin.Manifest.Id, out PluginStateRecord? state))
                {
                    state = new PluginStateRecord
                    {
                        PluginId  = plugin.Manifest.Id,
                        Enabled   = false,
                        SortOrder = states.Count == 0 ? 0 : states.Values.Max(item => item.SortOrder) + 1,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    await _infrastructure.Database.Global.Insertable(state).ExecuteCommandAsync(cancellationToken);
                    states[state.PluginId] = state;
                }

                plugin.Enabled   = state.Enabled;
                plugin.SortOrder = state.SortOrder;
                _plugins.Add(plugin);
            }

            _plugins.Sort((left, right) => left.SortOrder.CompareTo(right.SortOrder));
            UpdatePluginHealth();
            _logger.LogInformation("已发现 {Count} 个插件", _plugins.Count);
            return _plugins.ToDictionary(plugin => plugin.Manifest.Id, plugin => plugin.Error);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>接入 Sora 服务：成为普通事件的唯一订阅者。只调用一次，且在服务启动前。</summary>
    public void Attach(SoraService service)
    {
        _service = service;
        service.Events.OnEvent += DispatchAsync;
    }

    /// <summary>
    /// 激活已持久化为启用、但尚未初始化或上次初始化失败的插件；已激活的插件不受影响。
    /// 在机器人账号初始化完成后调用，之后在重新连接与数据库、RustFS 恢复时再次调用以重试失败的插件。
    /// 数据库暂停期间不做任何事。
    /// </summary>
    public async Task EnablePersistedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _accountReady = true;
            if (_suspended) return;

            foreach (LoadedPlugin plugin in _plugins.Where(item => item is { Enabled: true, Initialized: false, Error: null }))
                await ActivateAsync(plugin, cancellationToken);
            PublishDispatchOrder();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 启用插件并持久化。首次启用时登记指令并初始化；初始化失败的插件再次启用时重试初始化。
    /// 机器人账号就绪前只持久化启用状态，由 <see cref="EnablePersistedAsync" /> 在账号就绪后激活。
    /// </summary>
    /// <returns>失败原因；成功时为 null。</returns>
    public async Task<string?> EnableAsync(string pluginId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(pluginId) is not { } plugin) return $"插件 {pluginId} 不存在";
            if (_suspended) return SUSPENDED_ERROR;
            if (plugin.Error is not null) return plugin.Error;
            if (!_accountReady)
            {
                await SaveStatesAsync([StateOf(plugin, enabled: true, plugin.SortOrder)], cancellationToken);
                plugin.Enabled = true;
                return null;
            }

            // 先激活才知道能否启用；写库失败时撤销本次激活，内存与数据库保持一致。
            string? error = await ActivateAsync(plugin, cancellationToken);
            if (error is not null) return error;
            try
            {
                await SaveStatesAsync([StateOf(plugin, enabled: true, plugin.SortOrder)], cancellationToken);
            }
            catch (Exception) when (!plugin.Enabled)
            {
                Deactivate(plugin);
                throw;
            }

            plugin.Enabled = true;
            PublishDispatchOrder();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>停用插件并持久化：指令、事件订阅与后台任务从下一次触发起不再执行。</summary>
    /// <returns>失败原因；成功时为 null。</returns>
    public async Task<string?> DisableAsync(string pluginId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(pluginId) is not { } plugin) return $"插件 {pluginId} 不存在";
            if (_suspended) return SUSPENDED_ERROR;

            await SaveStatesAsync([StateOf(plugin, enabled: false, plugin.SortOrder)], cancellationToken);
            plugin.Enabled = false;
            Deactivate(plugin);
            PublishDispatchOrder();
            // 停用后不再重试初始化，它的初始化失败不再计入健康状态。
            UpdatePluginHealth();
            _logger.LogInformation("插件 {PluginId} 已停用", pluginId);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 设置插件顺序并持久化：同时决定跨插件的指令先后与普通事件订阅链顺序。未列出的插件排在最后，未知 ID 被忽略。
    /// 写库成功后才替换内存中的顺序。
    /// </summary>
    /// <param name="orderedIds">插件 ID，不得重复；由调用方在边界校验。</param>
    /// <returns>失败原因；成功时为 null。</returns>
    public async Task<string?> SetOrderAsync(IReadOnlyList<string> orderedIds, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_suspended) return SUSPENDED_ERROR;

            List<LoadedPlugin> ordered =
            [
                .. orderedIds.Select(Find).OfType<LoadedPlugin>(),
                .. _plugins.Where(plugin => !orderedIds.Contains(plugin.Manifest.Id))
            ];
            await SaveStatesAsync(ordered.Select((plugin, index) => StateOf(plugin, plugin.Enabled, index)), cancellationToken);

            _plugins.Clear();
            _plugins.AddRange(ordered);
            for (int i = 0; i < _plugins.Count; i++)
            {
                _plugins[i].SortOrder = i;
                _plugins[i].Registration?.SetPriority(PriorityOf(i));
            }

            PublishDispatchOrder();
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 数据库离线时关闭全部插件程序集、停止事件分发，保留各插件的启用状态以便恢复。
    /// </summary>
    public void Suspend()
    {
        _suspended = true;
        foreach (LoadedPlugin plugin in Volatile.Read(ref _dispatchOrder))
            plugin.Registration?.SetEnabled(false);
        _logger.LogWarning("数据库离线，已暂停全部插件");
    }

    /// <summary>数据库恢复后按停用前的状态重新启用插件。</summary>
    public void Resume()
    {
        _suspended = false;
        foreach (LoadedPlugin plugin in Volatile.Read(ref _dispatchOrder))
            plugin.Registration?.SetEnabled(true);
        _logger.LogInformation("数据库已恢复，插件按原状态恢复响应");
    }

    /// <summary>返回全部插件的状态。</summary>
    public async Task<IReadOnlyList<PluginSnapshot>> SnapshotAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            IReadOnlyList<CommandAssemblyState> assemblyStates = _service?.Commands.GetAssemblyStates() ?? [];
            return
            [
                .. _plugins.Select(plugin => new PluginSnapshot
                {
                    Id          = plugin.Manifest.Id,
                    Name        = plugin.Manifest.Name,
                    Version     = plugin.Manifest.Version,
                    Description = plugin.Manifest.Description,
                    Enabled     = plugin.Enabled,
                    Status      = StatusOf(plugin),
                    SortOrder   = plugin.SortOrder,
                    Error       = plugin.Error ?? plugin.InitError,
                    HasConfig   = plugin.ConfigSchema is not null,
                    HasSecrets  = plugin.Context?.SecretsType is not null,
                    PageTitle   = IsActive(plugin) ? plugin.Context?.PanelHost.Title : null,
                    PageEntry   = IsActive(plugin) ? plugin.Context?.PanelHost.Entry : null,
                    CommandTypes =
                    [
                        .. CommandTypesOf(plugin, assemblyStates).Select(state => new PluginCommandTypeSnapshot
                        {
                            Name         = state.Type.Name,
                            Enabled      = state.IsEffectivelyEnabled,
                            CommandCount = state.CommandCount
                        })
                    ]
                })
            ];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>返回插件的配置表单；插件不存在或未声明配置类型时返回 null。</summary>
    public async Task<PluginConfigView?> GetConfigAsync(string pluginId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(pluginId) is not { Context: { ConfigType: { } type } context, ConfigSchema: { } schema }) return null;

            return new PluginConfigView
            {
                Schema = schema.DeepClone(),
                Value  = JsonSerializer.SerializeToNode(context.LoadConfig(type), type, PluginConfigStore.JsonOptions)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>校验并保存插件配置，插件下次读取配置时得到新值。</summary>
    /// <returns>校验错误；成功时为空列表；插件不存在或未声明配置类型时返回 null。</returns>
    public async Task<IReadOnlyList<string>?> SaveConfigAsync(string pluginId, JsonNode? json, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(pluginId) is not { Context: { ConfigType: { } type } context }) return null;

            object? config = PluginConfigSchema.Parse(type, json, out IReadOnlyList<string> errors);
            if (config is null) return errors;

            await context.SaveConfigAsync(type, config, cancellationToken);
            _logger.LogInformation("插件 {PluginId} 的配置已更新", pluginId);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>返回插件机密各项的状态（不含值）；插件不存在或未声明机密类型时返回 null。</summary>
    /// <exception cref="JsonException">机密文件内容与机密类型不兼容。</exception>
    public async Task<IReadOnlyList<PluginSecretField>?> GetSecretsAsync(string pluginId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(pluginId) is not { Context: { SecretsType: { } type } context }) return null;

            return PluginSecretsSchema.Describe(type, context.SecretsHolder(type).Current);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>校验并写入插件机密，插件随即读到新值。日志只记录修改的项名。</summary>
    /// <returns>校验错误；成功时为空列表；插件不存在或未声明机密类型时返回 null。</returns>
    /// <exception cref="JsonException">机密文件内容与机密类型不兼容。</exception>
    public async Task<IReadOnlyList<string>?> SaveSecretsAsync(string pluginId, JsonObject changes, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Find(pluginId) is not { Context: { SecretsType: { } type } context }) return null;

            IReadOnlyList<string> errors = [];
            bool saved = await context.SecretsHolder(type).UpdateAsync(
                current => PluginSecretsSchema.Apply(type, current, changes, out errors),
                cancellationToken);
            if (!saved) return errors;

            _logger.LogInformation("插件 {PluginId} 的机密已更新：{Keys}", pluginId, string.Join("、", changes.Select(change => change.Key)));
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>返回启用中插件的专有面板；插件未启用或没有登记页面时返回 null。</summary>
    internal PluginPanel? FindActivePage(string pluginId) =>
        Find(pluginId) is { Context.PanelHost: { Title: not null } panel } plugin && IsActive(plugin) ? panel : null;

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (LoadedPlugin plugin in _plugins)
            plugin.Container?.Dispose();
    }

    private IEnumerable<LoadedPlugin> Discover()
    {
        string root = _settings.PluginRoot;
        if (!Directory.Exists(root))
        {
            _logger.LogInformation("插件目录 {Directory} 不存在，没有可加载的插件", root);
            yield break;
        }

        HashSet<string>            ids      = [];
        Dictionary<string, string> purposes = new();
        foreach (PluginPackageDirectory package in PluginPackageScanner.Scan(root))
        {
            if (package.Manifest is not { } manifest)
            {
                _logger.LogError("插件清单无效：{Reason}", package.Error);
                continue;
            }

            string directory = package.Directory;
            LoadedPlugin plugin = new()
            {
                Manifest  = manifest,
                Directory = directory
            };
            if (!ids.Add(manifest.Id))
            {
                _logger.LogError("插件 ID {PluginId} 重复，跳过 {Directory}", manifest.Id, directory);
                continue;
            }

            foreach (string purpose in manifest.BucketPurposes)
                if (!purposes.TryAdd(purpose, manifest.Id))
                    plugin.Error = $"bucket 用途 {purpose} 已被插件 {purposes[purpose]} 使用";
            yield return plugin;
        }
    }

    private void Load(LoadedPlugin plugin)
    {
        try
        {
            string entryPath = Path.Combine(plugin.Directory, plugin.Manifest.EntryAssembly);
            PluginLoadContext context = new(plugin.Manifest.Id, entryPath);
            Assembly assembly = context.LoadFromAssemblyPath(entryPath);
            Type[] entryTypes =
            [
                .. assembly.GetExportedTypes()
                           .Where(type => type is { IsClass: true, IsAbstract: false }
                                          && typeof(IShinanoPlugin).IsAssignableFrom(type))
            ];
            if (entryTypes.Length != 1)
                throw new InvalidOperationException($"入口程序集必须恰好包含一个 {nameof(IShinanoPlugin)} 实现，实际为 {entryTypes.Length} 个");

            IShinanoPlugin instance = (IShinanoPlugin)(Activator.CreateInstance(entryTypes[0])
                                                       ?? throw new InvalidOperationException("无法创建插件入口实例"));
            PluginContext pluginContext = CreateContext(plugin);
            // 静态指令组不需要实例；实例指令组由插件容器构造，构造函数可注入插件服务。
            Type[] commandTypes =
            [
                .. assembly.GetExportedTypes()
                           .Where(type => type is { IsClass: true, IsAbstract: false }
                                          && type.GetCustomAttribute<CommandGroupAttribute>() is not null)
            ];

            ContainerBuilder builder = new();
            builder.RegisterInstance(pluginContext).As<IPluginInitializationContext>();
            builder.RegisterInstance(_bot).As<IBotAccessor>();
            builder.RegisterInstance(_loggerFactory).As<ILoggerFactory>();
            builder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
            foreach (Type commandType in commandTypes)
                builder.RegisterType(commandType).AsSelf().SingleInstance();
            instance.ConfigureServices(builder);
            PluginPanelRegistration panel = new();
            instance.ConfigurePanel(panel);
            pluginContext.ConfigType  = panel.ConfigType;
            pluginContext.SecretsType = panel.SecretsType;

            plugin.Assembly     = assembly;
            plugin.Instance     = instance;
            plugin.Context      = pluginContext;
            plugin.CommandTypes = commandTypes;
            plugin.Container    = builder.Build();
            plugin.ConfigSchema = panel.ConfigType is null ? null : PluginConfigSchema.Create(panel.ConfigType);
            _logger.LogInformation("已加载插件 {PluginId} {Version}", plugin.Manifest.Id, plugin.Manifest.Version);
        }
        catch (Exception exception)
        {
            plugin.Error = exception.Message;
            _logger.LogError(exception, "加载插件 {PluginId} 失败", plugin.Manifest.Id);
        }
    }

    private PluginContext CreateContext(LoadedPlugin plugin)
    {
        string id = plugin.Manifest.Id;
        return new PluginContext(
            id,
            new PluginEvents(),
            new PluginScheduler(id, _scheduler),
            new PluginDatabase(id, _infrastructure.Database, _bot, _loggerFactory),
            new PluginMedia(id, _infrastructure.PersistentMedia, _infrastructure.Downloader),
            new PluginBuckets(
                plugin.Manifest.BucketPurposes,
                _settings.BucketPrefix,
                _infrastructure.Storage,
                _infrastructure.Downloader.DefaultLimitBytes),
            _bot,
            new PluginAssets(plugin.Directory),
            new PluginConfigStore(_infrastructure.Database),
            new PluginPanel(id, plugin.Directory, _endpoints, () => IsActive(plugin)),
            _fonts,
            _images,
            new PluginBrowser(id, _browser),
            _ffmpeg,
            _notices,
            _settings.DataDirectory,
            _infrastructure.TimeZone);
    }

    // 调用方持有 _gate。首次启用：关闭状态下登记程序集并注入指令实例、扫描、初始化，最后开启。
    // 初始化失败时程序集登记保留（处于关闭状态），重试只清空插件在初始化中登记的内容后重新初始化。
    private async Task<string?> ActivateAsync(LoadedPlugin plugin, CancellationToken cancellationToken)
    {
        if (plugin.Error is not null) return plugin.Error;
        if (_service is null) throw new InvalidOperationException("插件运行时尚未接入 Sora 服务");

        if (!plugin.Initialized)
        {
            try
            {
                if (plugin.Registration is null)
                {
                    // SAFE: Error 为 null 表示 Load 已成功设置以下成员。
                    AssemblyRegistration registration = _service.Commands.RegisterAssembly(
                        plugin.Assembly!,
                        PriorityOf(_plugins.IndexOf(plugin)),
                        enabled: false);
                    foreach (Type commandType in plugin.CommandTypes)
                        RegisterInstance(registration, commandType, plugin.Container!.Resolve(commandType));
                    registration.ScanCommandGroups();
                    plugin.Registration = registration;
                }

                _scheduler.SetOwnerEnabled(plugin.Manifest.Id, false);
                // SAFE: Error 为 null 的插件在加载时已建立能力集合与实例。
                PluginContext context = plugin.Context!;
                context.ResetInitialization();
                await plugin.Instance!.InitializeAsync(context, cancellationToken);
                context.EventsHost.Seal();
                context.PanelHost.Activate();
                plugin.Initialized = true;
                if (plugin.InitError is not null)
                {
                    plugin.InitError = null;
                    UpdatePluginHealth();
                }

                _scheduler.SetOwnerEnabled(plugin.Manifest.Id, true);
                context.SchedulerHost.Activate();
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                plugin.InitError = exception.Message;
                _scheduler.SetOwnerEnabled(plugin.Manifest.Id, false);
                UpdatePluginHealth();
                _logger.LogError(exception, "插件 {PluginId} 初始化失败，暂不启用；依赖恢复或重新启用时重试", plugin.Manifest.Id);
                return plugin.InitError;
            }
        }

        if (!_suspended) plugin.Registration?.SetEnabled(true);
        _scheduler.SetOwnerEnabled(plugin.Manifest.Id, true);
        _logger.LogInformation("插件 {PluginId} 已启用", plugin.Manifest.Id);
        // SAFE: Error 为 null 的插件在加载时已建立能力集合。
        plugin.Context!.NotifyEnabled(true, _logger);
        return null;
    }

    private async ValueTask DispatchAsync(BotEvent e)
    {
        if (_suspended) return;

        foreach (LoadedPlugin plugin in Volatile.Read(ref _dispatchOrder))
        {
            // SAFE: 分发顺序只包含已初始化的插件。
            foreach (PluginEventSubscription subscription in plugin.Context!.EventsHost.Subscriptions)
            {
                if (!subscription.EventType.IsInstanceOfType(e)) continue;

                try
                {
                    await subscription.Handler(e);
                }
                catch (Exception exception)
                {
                    _logger.LogError(
                        exception,
                        "插件 {PluginId} 处理 {EventType} 失败",
                        plugin.Manifest.Id,
                        e.GetType().Name);
                }

                if (!e.IsContinueEventChain) return;
            }
        }
    }

    // 调用方持有 _gate。
    private void PublishDispatchOrder() =>
        Volatile.Write(ref _dispatchOrder, [.. _plugins.Where(plugin => plugin is { Enabled: true, Initialized: true })]);

    // 排序一次写多行，放在同一事务中，失败时不留下一半新顺序。
    private async Task SaveStatesAsync(IEnumerable<PluginStateRecord> states, CancellationToken cancellationToken)
    {
        SqlSugarScope db = _infrastructure.Database.Global;
        List<PluginStateRecord> records = [.. states];
        await db.InTransactionAsync(() => db.Storageable(records).ExecuteCommandAsync(cancellationToken));
    }

    private static PluginStateRecord StateOf(LoadedPlugin plugin, bool enabled, int sortOrder) =>
        new()
        {
            PluginId  = plugin.Manifest.Id,
            Enabled   = enabled,
            SortOrder = sortOrder,
            UpdatedAt = DateTimeOffset.UtcNow
        };

    // 调用方持有 _gate。关闭指令、后台任务并通知插件；不改持久化状态与分发顺序。
    private void Deactivate(LoadedPlugin plugin)
    {
        plugin.Registration?.SetEnabled(false);
        _scheduler.SetOwnerEnabled(plugin.Manifest.Id, false);
        // SAFE: 已初始化的插件必有能力集合。
        if (plugin.Initialized) plugin.Context!.NotifyEnabled(false, _logger);
    }

    private void UpdatePluginHealth()
    {
        string[] failed = [.. _plugins.Where(plugin => plugin.Error is not null || plugin is { Enabled: true, InitError: not null }).Select(plugin => plugin.Manifest.Id)];
        _health.Set(
            HealthComponent.Plugins,
            failed.Length == 0 ? HealthLevel.Normal : HealthLevel.Degraded,
            failed.Length == 0 ? string.Empty : $"插件加载或初始化失败：{string.Join("、", failed)}");
    }

    private LoadedPlugin? Find(string pluginId) => _plugins.Find(plugin => plugin.Manifest.Id == pluginId);

    private static IEnumerable<CommandTypeState> CommandTypesOf(
        LoadedPlugin                        plugin,
        IReadOnlyList<CommandAssemblyState> assemblyStates) =>
        assemblyStates.FirstOrDefault(state => state.Assembly == plugin.Assembly)?.Types ?? [];

    // 专有面板与插件 API 只在插件处于启用状态时可用。
    private static bool IsActive(LoadedPlugin plugin) => plugin is { Enabled: true, Initialized: true, Error: null };

    private static PluginStatus StatusOf(LoadedPlugin plugin) =>
        plugin switch
        {
            { Error: not null }     => PluginStatus.LoadFailed,
            { Enabled: false }      => PluginStatus.Disabled,
            { Initialized: true }   => PluginStatus.Active,
            { InitError: not null } => PluginStatus.InitFailed,
            _                       => PluginStatus.Pending
        };

    // 顺序靠前的插件优先级更高；宿主自身的运维指令使用更高的固定优先级。
    private int PriorityOf(int index) => _plugins.Count - index;

    private static void RegisterInstance(AssemblyRegistration registration, Type commandType, object instance)
    {
        try
        {
            RegisterInstanceMethod.MakeGenericMethod(commandType).Invoke(registration, [instance]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Throw(exception.InnerException);
        }
    }
}
