using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Shinano.Host.Web;

/// <summary>校验后实际使用的管理面板监听端点。</summary>
public sealed class WebEndpoint
{
    /// <summary>监听地址。</summary>
    public required IPAddress Address { get; init; }

    /// <summary>监听端口。</summary>
    public required int Port { get; init; }

    /// <summary>HTTPS 证书；为 null 时使用 HTTP。</summary>
    public X509Certificate2? Certificate { get; init; }

    /// <summary>
    /// 配置无效而回退到回环地址时的原因；配置有效时为空。
    /// </summary>
    public IReadOnlyList<string> Problems { get; init; } = [];
}
