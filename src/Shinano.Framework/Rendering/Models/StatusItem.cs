namespace Shinano.Framework.Rendering.Models;

/// <summary>状态报告中的一项检查。</summary>
public sealed class StatusItem
{
    /// <summary>等级：正常为绿色、警告为橙色、错误为红色。</summary>
    public required CardLevel Level { get; init; }

    /// <summary>检查项名称。</summary>
    public required string Name { get; init; }

    /// <summary>结果，如「正常」「312 MB」。</summary>
    public required string Value { get; init; }

    /// <summary>补充说明，如延迟或上限；没有时为 null。</summary>
    public string? Detail { get; init; }
}
