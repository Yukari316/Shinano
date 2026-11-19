namespace Shinano.Plugin.ImageSources;

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

/// <summary>一次搜索的状态。</summary>
public enum SearchStatus
{
    /// <summary>搜索完成，结果可能为空。</summary>
    Ok,

    /// <summary>搜索服务的额度用完（SauceNAO 429、trace.moe 402）。</summary>
    RateLimited,

    /// <summary>插件没有令牌、令牌被拒，或网关没有该搜索服务的凭据。</summary>
    Unconfigured,

    /// <summary>网关、网络或搜索服务出错，包括 IQDB 的排队失败页。</summary>
    Failed
}
