namespace Shinano.Host.Notifications;

/// <summary>通知卡片中的一个条目。</summary>
public sealed class NotificationItem
{
    /// <summary>条目名称。</summary>
    public required string Label { get; init; }

    /// <summary>条目内容；过长时在卡片中截断，完整内容以日志为准。</summary>
    public required string Value { get; init; }
}
