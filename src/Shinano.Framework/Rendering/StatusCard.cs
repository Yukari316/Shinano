namespace Shinano.Framework.Rendering;

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

/// <summary>状态报告中的一项检查。</summary>
public sealed class StatusItem
{
    /// <summary>等级：正常为绿色、警告为橙色、错误为红色。</summary>
    public required CardLevel Level { get; init; }

    /// <summary>检查项名称。</summary>
    public required string Name { get; init; }

    /// <summary>结果，如「正常」「312 MB」；过长时换行。</summary>
    public required string Value { get; init; }

    /// <summary>补充说明，如延迟或上限；没有时为 null。与结果放得下同一行时右对齐，否则另起一行并换行。</summary>
    public string? Detail { get; init; }
}
