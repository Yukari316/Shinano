namespace Shinano.Host.Bot.Models;

/// <summary>通知卡片的等级，对应面板语义色 error、warning、success。</summary>
public enum NotificationLevel
{
    /// <summary>错误：功能已停止或操作失败。</summary>
    Error,

    /// <summary>警告：功能降级或需要关注。</summary>
    Warning,

    /// <summary>正常：已恢复或已完成。</summary>
    Normal
}
