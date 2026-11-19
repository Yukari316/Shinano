using Shinano.Host.Plugins;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;

namespace Shinano.Host.Bot;

/// <summary>
/// 对健康变化的宿主级反应：数据库离线时暂停全部插件与后台任务、恢复时还原；
/// 数据库与 RustFS 的故障开始和恢复各通知全部 Super User 一次；
/// 已有机器人账号后，数据库离线与 LLBot 断线的区间登记为归档缺口；
/// 数据库或 RustFS 恢复时重试未完成的账号初始化与插件初始化。
/// </summary>
public sealed class HealthReactions : IHostedService
{
    private readonly RuntimeHealth     _health;
    private readonly PluginRuntime     _plugins;
    private readonly HostScheduler     _scheduler;
    private readonly BotAccessor       _bot;
    private readonly ArchiveGapTracker _gaps;
    private readonly SuperUserNotifier _notifier;
    private readonly AccountActivation _activation;

    /// <summary>创建反应处理。</summary>
    public HealthReactions(
        RuntimeHealth     health,
        PluginRuntime     plugins,
        HostScheduler     scheduler,
        BotAccessor       bot,
        ArchiveGapTracker gaps,
        SuperUserNotifier notifier,
        AccountActivation activation)
    {
        _health     = health;
        _plugins    = plugins;
        _scheduler  = scheduler;
        _bot        = bot;
        _gaps       = gaps;
        _notifier   = notifier;
        _activation = activation;
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
        bool failing = current.Level != HealthLevel.Normal;
        switch (current.Component)
        {
            case HealthComponent.Database:
                _scheduler.Paused = failing;
                if (failing) _plugins.Suspend();
                else _plugins.Resume();
                TrackGap(ArchiveGapCause.DatabaseOffline, failing, current.Since);
                _notifier.Notify(failing
                    ? Card(NotificationLevel.Error, "DB-OFFLINE", "数据库不可用", current.Detail, "Bot 暂停响应全部功能")
                    : Card(NotificationLevel.Normal, "DB-RECOVERED", "数据库已恢复", null, "已恢复响应"));
                if (!failing) _ = _activation.RunAsync(CancellationToken.None);
                break;
            case HealthComponent.ObjectStorage:
                _notifier.Notify(failing
                    ? Card(NotificationLevel.Warning, "RUSTFS-OFFLINE", "RustFS 不可用", current.Detail, "媒体功能暂不可用")
                    : Card(NotificationLevel.Normal, "RUSTFS-RECOVERED", "RustFS 已恢复", null, "媒体功能已恢复"));
                if (!failing) _ = _activation.RunAsync(CancellationToken.None);
                break;
            case HealthComponent.Bot:
                TrackGap(ArchiveGapCause.BotDisconnected, failing, current.Since);
                break;
        }
    }

    private static NotificationCard Card(NotificationLevel level, string code, string title, string? cause, string impact) =>
        new()
        {
            Level  = level,
            Code   = code,
            Title  = title,
            Cause  = string.IsNullOrWhiteSpace(cause) ? null : cause,
            Impact = impact
        };

    // 首次连接前没有机器人账号，也就没有可归档的会话，不登记缺口。
    private void TrackGap(ArchiveGapCause cause, bool failing, DateTimeOffset at)
    {
        if (failing && _bot.SelfId != 0) _gaps.Open(cause, at);
        else if (!failing) _gaps.Close(cause, at);
    }
}
