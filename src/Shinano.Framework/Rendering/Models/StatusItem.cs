namespace Shinano.Framework.Rendering.Models;

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
