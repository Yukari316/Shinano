namespace Shinano.Framework.Rendering.Models;

/// <summary>状态报告卡片：如健康检查结果。</summary>
public sealed class StatusCard
{
    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>检查时间，按其自身的时区偏移显示到秒。</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>各检查项。</summary>
    public required IReadOnlyList<StatusItem> Items { get; init; }

    /// <summary>页脚右侧的自定义文字；为 null 时不显示。</summary>
    public string? FooterRight { get; init; }
}
