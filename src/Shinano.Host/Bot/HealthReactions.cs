using Microsoft.Extensions.Logging;
using Shinano.Host.Plugins;
using Shinano.Infrastructure.Configuration;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Scheduling;
using Sora.Entities.Interfaces;

namespace Shinano.Host.Bot;

/// <summary>
/// 对健康变化的宿主级反应：数据库离线时暂停全部插件与后台任务、恢复时还原；
/// 数据库与 RustFS 的故障开始和恢复各通知全部 Super User 一次。
/// </summary>
public sealed class HealthReactions : IHostedService
{
    private readonly RuntimeHealth            _health;
    private readonly PluginRuntime            _plugins;
    private readonly HostScheduler            _scheduler;
    private readonly BotAccessor              _bot;
    private readonly long[]                   _superUsers;
    private readonly ILogger<HealthReactions> _logger;

    /// <summary>创建反应处理。</summary>
    public HealthReactions(
        RuntimeHealth            health,
        PluginRuntime            plugins,
        HostScheduler            scheduler,
        BotAccessor              bot,
        DeploymentOptions        deployment,
        ILogger<HealthReactions> logger)
    {
        _health     = health;
        _plugins    = plugins;
        _scheduler  = scheduler;
        _bot        = bot;
        _superUsers = deployment.SuperUsers;
        _logger     = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _health.Changed += OnChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        _health.Changed -= OnChanged;
        return Task.CompletedTask;
    }

    private void OnChanged(HealthChange change)
    {
        HealthStatus current = change.Current;
        switch (current.Component)
        {
            case HealthComponent.Database:
                bool offline = current.Level != HealthLevel.Normal;
                _scheduler.Paused = offline;
                if (offline) _plugins.Suspend();
                else _plugins.Resume();
                Notify(offline ? $"[Shinano] 数据库不可用：{current.Detail}" : "[Shinano] 数据库已恢复");
                break;
            case HealthComponent.ObjectStorage:
                Notify(current.Level != HealthLevel.Normal
                    ? $"[Shinano] RustFS 不可用，媒体功能受影响：{current.Detail}"
                    : "[Shinano] RustFS 已恢复");
                break;
        }
    }

    private void Notify(string text)
    {
        IBotApi? api = _bot.Api;
        if (api is null)
        {
            _logger.LogWarning("未连接 LLBot，无法通知 Super User：{Text}", text);
            return;
        }

        _ = SendAsync(api, text);
    }

    private async Task SendAsync(IBotApi api, string text)
    {
        foreach (long user in _superUsers)
        {
            try
            {
                await api.SendFriendMessageAsync(user, text);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "通知 Super User {UserId} 失败", user);
            }
        }
    }
}
