using Shinano.Host.Notifications;
using Shinano.Host.Plugins;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Scheduling;

namespace Shinano.Host.Bot;

/// <summary>
/// 对健康变化的宿主级反应：数据库离线时暂停全部插件与后台任务、恢复时还原；
/// 数据库与 RustFS 的故障开始和恢复各通知全部 Super User 一次；
/// 已有机器人账号后，数据库离线与 LLBot 断线的区间登记为归档缺口。
/// </summary>
public sealed class HealthReactions : IHostedService
{
    private readonly RuntimeHealth     _health;
    private readonly PluginRuntime     _plugins;
    private readonly HostScheduler     _scheduler;
    private readonly BotAccessor       _bot;
    private readonly ArchiveGapTracker _gaps;
    private readonly SuperUserNotifier _notifier;

    /// <summary>创建反应处理。</summary>
    public HealthReactions(
        RuntimeHealth     health,
        PluginRuntime     plugins,
        HostScheduler     scheduler,
        BotAccessor       bot,
        ArchiveGapTracker gaps,
        SuperUserNotifier notifier)
    {
        _health    = health;
        _plugins   = plugins;
        _scheduler = scheduler;
        _bot       = bot;
        _gaps      = gaps;
        _notifier  = notifier;
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
                    ? Card(NotificationLevel.Error, "数据库不可用", "影响", "已停止响应全部功能，恢复后自动继续", current.Detail)
                    : Card(NotificationLevel.Normal, "数据库已恢复", "影响", "已恢复响应，离线期间的消息登记为归档缺口", null));
                break;
            case HealthComponent.ObjectStorage:
                _notifier.Notify(failing
                    ? Card(NotificationLevel.Warning, "RustFS 不可用", "影响", "聊天媒体标记为缺失，需要媒体存储的功能报错", current.Detail)
                    : Card(NotificationLevel.Normal, "RustFS 已恢复", "影响", "媒体功能已恢复", null));
                break;
            case HealthComponent.Bot:
                TrackGap(ArchiveGapCause.BotDisconnected, failing, current.Since);
                break;
        }
    }

    private static NotificationCard Card(NotificationLevel level, string title, string label, string value, string? reason)
    {
        List<NotificationItem> items = [new NotificationItem { Label = label, Value = value }];
        if (!string.IsNullOrWhiteSpace(reason)) items.Add(new NotificationItem { Label = "原因", Value = reason });
        return new NotificationCard
        {
            Level = level,
            Title = title,
            Items = items
        };
    }

    // 首次连接前没有机器人账号，也就没有可归档的会话，不登记缺口。
    private void TrackGap(ArchiveGapCause cause, bool failing, DateTimeOffset at)
    {
        if (failing && _bot.SelfId != 0) _gaps.Open(cause, at);
        else if (!failing) _gaps.Close(cause, at);
    }
}
