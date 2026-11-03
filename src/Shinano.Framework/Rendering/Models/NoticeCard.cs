namespace Shinano.Framework.Rendering.Models;

/// <summary>通知卡片：如宿主发给 Super User 的故障与恢复提醒。</summary>
public sealed class NoticeCard
{
    /// <summary>等级。</summary>
    public required CardLevel Level { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>发生时间，按其自身的时区偏移显示到秒。</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>正文各行，如原因与影响。</summary>
    public required IReadOnlyList<CardRow> Rows { get; init; }

    /// <summary>页脚左侧的英文代号，用于区分通知种类，如 <c>DB-OFFLINE</c>。</summary>
    public required string Code { get; init; }

    /// <summary>页脚右侧的自定义文字；为 null 时不显示。</summary>
    public string? FooterRight { get; init; }
}
