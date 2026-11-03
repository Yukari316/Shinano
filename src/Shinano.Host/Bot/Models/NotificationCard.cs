namespace Shinano.Host.Bot.Models;

/// <summary>
/// 发给 Super User 的通知卡片内容。正文只写原因与影响，影响用一句话写明；详细信息以日志为准。不放路径、凭据与 DSN。
/// </summary>
public sealed class NotificationCard
{
    /// <summary>等级。</summary>
    public required NotificationLevel Level { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>通知种类的英文代号，显示在卡片页脚左侧，如 <c>DB-OFFLINE</c>。</summary>
    public required string Code { get; init; }

    /// <summary>原因；恢复类通知没有原因时为 null。</summary>
    public string? Cause { get; init; }

    /// <summary>影响，一句话。</summary>
    public required string Impact { get; init; }

    /// <summary>卡片无法绘制时代替图片发送的文字。</summary>
    public string ToText() =>
        string.Join('\n', (string[]) [$"[Shinano] {Title}", .. Cause is null ? [] : (string[]) [$"原因：{Cause}"], $"影响：{Impact}"]);
}
