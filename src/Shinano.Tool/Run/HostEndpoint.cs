using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shinano.Tool.Configuration;

namespace Shinano.Tool.Run;

/// <summary>
/// 按部署配置解析宿主面板的访问地址，规则与宿主相同：拒绝全部网卡地址；配置无效，或配置了证书却无法加载时，
/// 宿主回退到 <c>127.0.0.1</c> 的 HTTP。
/// </summary>
public sealed class HostEndpoint
{
    private const string LOCALHOST = "localhost";
    private const int    MIN_PORT  = 1;
    private const int    MAX_PORT  = 65535;

    /// <summary>就绪接口地址。</summary>
    public required string ReadinessUrl { get; init; }

    /// <summary>启用 HTTPS 时证书的 SHA-256 指纹，只信任该证书；HTTP 时为 null。</summary>
    public string? Thumbprint { get; init; }

    /// <summary>解析配置节 <paramref name="section" /> 的监听配置。</summary>
    /// <param name="config">部署配置。</param>
    /// <param name="section">监听配置节，如 <see cref="ConfigKeys.WEB" />。</param>
    /// <param name="defaultPort">端口未配置或无效时使用的端口。</param>
    /// <param name="dataDirectory">部署数据目录，用于解析证书的相对路径。</param>
    public static HostEndpoint Resolve(DeploymentConfig config, string section, int defaultPort, string dataDirectory)
    {
        int port = config.GetInt(ConfigKeys.In(section, ConfigKeys.PORT)) is { } configured and >= MIN_PORT and <= MAX_PORT
            ? configured
            : defaultPort;
        string text = config.GetString(ConfigKeys.In(section, ConfigKeys.LISTEN_ADDRESS)) ?? IPAddress.Loopback.ToString();
        IPAddress? address = text.Equals(LOCALHOST, StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.TryParse(text, out IPAddress? parsed) ? parsed : null;
        if (address is null || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return Http(IPAddress.Loopback, port);

        string? certificatePath = config.GetString(ConfigKeys.In(section, ConfigKeys.CERTIFICATE_PATH));
        if (certificatePath is null) return Http(address, port);

        try
        {
            string path = Path.GetFullPath(certificatePath, dataDirectory);
            if (!File.Exists(path)) return Http(IPAddress.Loopback, port);
            using X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(
                path,
                config.GetString(ConfigKeys.In(section, ConfigKeys.CERTIFICATE_PASSWORD)));
            if (!certificate.HasPrivateKey) return Http(IPAddress.Loopback, port);
            return new HostEndpoint
            {
                ReadinessUrl = $"https://{Format(address)}:{port}{HostContract.READINESS_PATH}",
                Thumbprint   = certificate.GetCertHashString(HashAlgorithmName.SHA256)
            };
        }
        catch (CryptographicException)
        {
            return Http(IPAddress.Loopback, port);
        }
    }

    private static HostEndpoint Http(IPAddress address, int port) =>
        new() { ReadinessUrl = $"http://{Format(address)}:{port}{HostContract.READINESS_PATH}" };

    private static string Format(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
}
