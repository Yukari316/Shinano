using Microsoft.Extensions.Logging;
using Shinano.Infrastructure;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Sora.Entities.Events;
using Sora.Entities.Filters;

namespace Shinano.Host.Bot;

/// <summary>
/// 宿主的事件前置过滤器，先于指令与插件执行，不阻断任何事件：
/// 跟踪 LLBot 连接状态，并在收到消息时按节流间隔在后台探测数据库与 RustFS。
/// 连接事件在这里处理而不走普通事件链，避免被插件终止。
/// </summary>
internal sealed class HostEventFilter : IEventPreFilter
{
    private readonly HostInfrastructure       _infrastructure;
    private readonly RuntimeHealth            _health;
    private readonly BotAccessor              _bot;
    private readonly Func<Task>               _onConnected;
    private readonly ILogger<HostEventFilter> _logger;
    private          long                     _accountSelfId;

    /// <summary>创建过滤器。</summary>
    /// <param name="infrastructure">基础设施，已就绪。</param>
    /// <param name="health">运行健康状态。</param>
    /// <param name="bot">当前连接。</param>
    /// <param name="onConnected">每次连接到本进程的账号后在后台执行。</param>
    /// <param name="logger">日志。</param>
    public HostEventFilter(
        HostInfrastructure       infrastructure,
        RuntimeHealth            health,
        BotAccessor              bot,
        Func<Task>               onConnected,
        ILogger<HostEventFilter> logger)
    {
        _infrastructure = infrastructure;
        _health         = health;
        _bot            = bot;
        _onConnected    = onConnected;
        _logger         = logger;
    }

    /// <summary>先于其他过滤器执行。</summary>
    public int Order => int.MinValue;

    /// <inheritdoc />
    public ValueTask<bool> OnEventAsync(BotEvent e, CancellationToken ct)
    {
        switch (e)
        {
            case ConnectedEvent connected:
                OnConnected(connected);
                break;
            case DisconnectedEvent disconnected:
                _bot.SetDisconnected();
                _health.Set(HealthComponent.Bot, HealthLevel.Stopped, $"LLBot 连接断开：{disconnected.Reason}");
                break;
            case MessageReceivedEvent:
                _ = ProbeAsync(_infrastructure.DatabaseAvailability);
                _ = ProbeAsync(_infrastructure.StorageAvailability);
                break;
        }

        return ValueTask.FromResult(true);
    }

    private void OnConnected(ConnectedEvent e)
    {
        long selfId = e.SelfId;
        long first  = Interlocked.CompareExchange(ref _accountSelfId, selfId, 0);
        if (first != 0 && first != selfId)
        {
            // 一个进程只服务一个账号；账号相关的 schema 与插件状态都按首个账号建立。
            _logger.LogError("LLBot 账号从 {Previous} 变为 {Current}，切换账号需要重启进程", first, selfId);
            _health.Set(HealthComponent.Bot, HealthLevel.Stopped, "LLBot 账号与本进程初始化的账号不一致，需要重启");
            return;
        }

        _bot.SetConnected(e.Api, selfId);
        _health.Set(HealthComponent.Bot, HealthLevel.Normal, string.Empty);
        // 每次连接都执行：上次账号或插件初始化失败时借此重试，已就绪时只检查有无待激活的插件。
        _ = _onConnected();
    }

    private async Task ProbeAsync(DependencyAvailability availability)
    {
        try
        {
            await availability.CheckAsync(force: false, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "依赖探测异常");
        }
    }
}
