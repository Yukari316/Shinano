namespace Shinano.Plugin.ImageSearch.Models;

/// <summary>一个搜索服务的结果。</summary>
public sealed class SearchResult
{
    /// <summary>状态。</summary>
    public required SearchStatus Status { get; init; }

    /// <summary>结果，按相似度从高到低；状态不是 <see cref="SearchStatus.Ok" /> 时为空。</summary>
    public IReadOnlyList<SearchHit> Hits { get; init; } = [];

    /// <summary>用于日志的说明，如错误原因或剩余额度；没有时为空。</summary>
    public string Detail { get; init; } = string.Empty;

    /// <summary>创建没有结果的失败状态。</summary>
    public static SearchResult Of(SearchStatus status, string detail) =>
        new()
        {
            Status = status,
            Detail = detail
        };
}
