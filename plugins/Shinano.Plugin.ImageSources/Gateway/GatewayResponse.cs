namespace Shinano.Plugin.ImageSources.Gateway;

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

/// <summary>一次图源服务调用的结果类别。</summary>
public enum GatewayOutcome
{
    /// <summary>上游的响应（带 <c>x-yukari-upstream</c> 头），状态码与正文为上游原样。</summary>
    Upstream,

    /// <summary>网关拒绝了令牌，或插件没有配置令牌。</summary>
    Unauthorized,

    /// <summary>网关没有配置该上游的凭据（503）。</summary>
    Unconfigured,

    /// <summary>网关或网络故障：上游不可达、超时、被质询、文件过大等。</summary>
    Failed
}
