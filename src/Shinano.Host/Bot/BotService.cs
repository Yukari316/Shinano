using Microsoft.Extensions.Logging;
using Shinano.Host.Archive;
using Shinano.Host.Plugins;
using Shinano.Infrastructure;
using Shinano.Infrastructure.Configuration;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Logging;
using Sora;
using Sora.Adapter.Milky;
using Sora.Command;
using Sora.Core.Types;

namespace Shinano.Host.Bot;

/// <summary>
/// Bot 的生命周期：基础设施就绪后加载插件、创建并启动 Sora 服务并登记宿主唯一的消息记录器；
/// 首次连接得到机器人账号后初始化账号数据并按持久化状态启用插件。
/// 基础设施进入故障模式时不连接 LLBot。
/// </summary>
public sealed class BotService : BackgroundService
{
    private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(5);

    private readonly HostInfrastructure  _infrastructure;
    private readonly PluginRuntime       _plugins;
    private readonly ChatArchiveService  _archive;
    private readonly RuntimeHealth       _health;
    private readonly BotAccessor         _bot;
    private readonly LlBotOptions        _options;
    private readonly DeploymentOptions   _deployment;
    private readonly LogFileLayout       _logLayout;
    private readonly LoggingOptions      _loggingOptions;
    private readonly ILoggerFactory      _loggerFactory;
    private readonly ILogger<BotService> _logger;
    private          SoraService?        _service;

    /// <summary>创建服务。</summary>
    public BotService(
        HostInfrastructure infrastructure,
        PluginRuntime      plugins,
        ChatArchiveService archive,
        RuntimeHealth      health,
        BotAccessor        bot,
        LlBotOptions       options,
        DeploymentOptions  deployment,
        LogFileLayout      logLayout,
        LoggingOptions     loggingOptions,
        ILoggerFactory     loggerFactory)
    {
        _infrastructure = infrastructure;
        _plugins        = plugins;
        _archive        = archive;
        _health         = health;
        _bot            = bot;
        _options        = options;
        _deployment     = deployment;
        _logLayout      = logLayout;
        _loggingOptions = loggingOptions;
        _loggerFactory  = loggerFactory;
        _logger         = loggerFactory.CreateLogger<BotService>();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _health.Set(HealthComponent.Bot, HealthLevel.Stopped, "尚未连接 LLBot");
        if (!await _infrastructure.Ready.WaitAsync(stoppingToken))
        {
            _logger.LogError("基础设施处于故障模式，Bot 不连接 LLBot");
            return;
        }

        await _plugins.LoadAsync(stoppingToken);
        _service = SoraServiceFactory.Instance.CreateMilkyService(
            new MilkyConfig
            {
                Host              = _options.Host,
                Port              = _options.Port,
                Prefix            = _options.Prefix,
                AccessToken       = _options.AccessToken,
                EventTransport    = EventTransport.WebSocket,
                ReconnectInterval = ReconnectInterval,
                SuperUsers        = [.. _deployment.SuperUsers.Select(user => (UserId)user)]
            });

        AssemblyRegistration hostCommands = _service.Commands.RegisterAssembly(
            typeof(HostCommands).Assembly,
            int.MaxValue,
            enabled: true);
        hostCommands.RegisterCommandInstance(
            new HostCommands(_infrastructure, _health, _plugins, _logLayout, _loggingOptions.MaxTotalSizeMiB));
        hostCommands.ScanCommandGroups();
        _plugins.Attach(_service);
        _service.UseMessageRecorder(_archive);
        _service.UseEventPreFilter(
            new HostEventFilter(
                _infrastructure,
                _health,
                _bot,
                selfId => InitializeAccountAsync(selfId, stoppingToken),
                _loggerFactory.CreateLogger<HostEventFilter>()));

        await _service.StartAsync(stoppingToken);
        _logger.LogInformation("正在连接 LLBot {Host}:{Port}", _options.Host, _options.Port);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_service is not null)
        {
            await _service.StopAsync(cancellationToken);
            await _service.DisposeAsync();
        }
    }

    private async Task InitializeAccountAsync(long selfId, CancellationToken cancellationToken)
    {
        await _infrastructure.InitializeAccountAsync(selfId, cancellationToken);
        await _plugins.EnablePersistedAsync(cancellationToken);
    }
}
