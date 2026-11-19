using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Host.Bot;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>插件经宿主通知器通知 Super User。</summary>
internal sealed class PluginNotices : IPluginNotices
{
    private readonly SuperUserNotifier _notifier;

    /// <summary>创建通知入口。</summary>
    public PluginNotices(SuperUserNotifier notifier)
    {
        _notifier = notifier;
    }

    /// <inheritdoc />
    public void NotifySuperUsers(SuperUserNotice notice) =>
        _notifier.Notify(
            new NotificationCard
            {
                Level = notice.Level switch
                {
                    CardLevel.Error   => NotificationLevel.Error,
                    CardLevel.Warning => NotificationLevel.Warning,
                    _                 => NotificationLevel.Normal
                },
                Title  = notice.Title,
                Code   = notice.Code,
                Cause  = notice.Cause,
                Impact = notice.Impact
            });
}
