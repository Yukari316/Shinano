namespace Shinano.Host.Notifications;

/// <summary>发给 Super User 的通知卡片内容。不放路径、凭据与 DSN。</summary>
public sealed class NotificationCard
{
    /// <summary>等级。</summary>
    public required NotificationLevel Level { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>条目，按顺序显示。</summary>
    public IReadOnlyList<NotificationItem> Items { get; init; } = [];

    /// <summary>卡片无法绘制时代替图片发送的文字。</summary>
    public string ToText() =>
        string.Join('\n', [$"[Shinano] {Title}", .. Items.Select(item => $"{item.Label}：{item.Value}")]);
}
