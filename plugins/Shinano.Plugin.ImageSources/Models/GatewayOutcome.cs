namespace Shinano.Plugin.ImageSources.Models;

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
