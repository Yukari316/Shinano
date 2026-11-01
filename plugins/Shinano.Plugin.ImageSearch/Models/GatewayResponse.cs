namespace Shinano.Plugin.ImageSearch.Models;

/// <summary>图源服务搜索接口的响应。</summary>
public sealed class GatewayResponse
{
    /// <summary>结果类别。</summary>
    public required GatewayOutcome Outcome { get; init; }

    /// <summary>HTTP 状态码；网络故障时为 0。</summary>
    public int StatusCode { get; init; }

    /// <summary>响应正文；上游响应为原样文本，其余为网关的错误说明。</summary>
    public string Body { get; init; } = string.Empty;
}
