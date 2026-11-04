namespace Shinano.Plugin.ImageSources.Models;

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
