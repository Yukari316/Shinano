using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Shinano.Monitor.Web.Options;

namespace Shinano.Monitor.Web;

/// <summary>
/// 解析后的监听端点。规则与主进程面板相同（为保持监控独立，在本工程内单独实现）：拒绝全部网卡地址；
/// HTTPS 可选；配置无效，或配置了证书却无法加载时，回退到 <c>127.0.0.1</c> 的 HTTP 并给出原因。
/// </summary>
public sealed class ListenEndpoint
{
    private const int    MIN_PORT  = 1;
    private const int    MAX_PORT  = 65535;
    private const string LOCALHOST = "localhost";

    /// <summary>监听地址。</summary>
    public required IPAddress Address { get; init; }

    /// <summary>端口。</summary>
    public required int Port { get; init; }

    /// <summary>HTTPS 证书；为 null 时使用 HTTP。</summary>
    public X509Certificate2? Certificate { get; init; }

    /// <summary>配置无效的原因；有效时为空。</summary>
    public IReadOnlyList<string> Problems { get; init; } = [];

    /// <summary>访问该端点的基地址，如 <c>https://10.0.0.5:19100</c>。</summary>
    public string BaseUrl
    {
        get
        {
            string host = Address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{Address}]" : Address.ToString();
            return $"{(Certificate is null ? "http" : "https")}://{host}:{Port}";
        }
    }

    /// <summary>证书的 SHA-256 指纹；HTTP 时为 null。</summary>
    public string? Thumbprint => Certificate?.GetCertHashString(HashAlgorithmName.SHA256);

    /// <summary>解析监听配置。</summary>
    /// <param name="options">监听配置。</param>
    /// <param name="defaultPort">端口未配置或无效时使用的端口。</param>
    /// <param name="dataDirectory">部署数据目录，用于解析证书的相对路径。</param>
    public static ListenEndpoint Resolve(ListenOptions options, int defaultPort, string dataDirectory)
    {
        List<string> problems = [];
        int port = options.Port;
        if (port is < MIN_PORT or > MAX_PORT)
        {
            if (port != 0) problems.Add($"端口 {port} 超出范围，改用 {defaultPort}");
            port = defaultPort;
        }

        string text = options.ListenAddress.Trim();
        IPAddress? address = text.Equals(LOCALHOST, StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.TryParse(text, out IPAddress? parsed) ? parsed : null;
        if (address is null) return Fallback(port, [.. problems, $"无法解析监听地址 \"{options.ListenAddress}\""]);
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return Fallback(port, [.. problems, $"不允许监听全部网卡地址 {address}"]);

        if (string.IsNullOrWhiteSpace(options.CertificatePath))
            return new ListenEndpoint
            {
                Address  = address,
                Port     = port,
                Problems = problems
            };

        string path = Path.GetFullPath(options.CertificatePath, dataDirectory);
        try
        {
            if (!File.Exists(path)) return Fallback(port, [.. problems, $"证书文件不存在：{path}"]);
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(path, options.CertificatePassword);
            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                return Fallback(port, [.. problems, $"证书 {path} 不含私钥"]);
            }

            return new ListenEndpoint
            {
                Address     = address,
                Port        = port,
                Certificate = certificate,
                Problems    = problems
            };
        }
        catch (CryptographicException exception)
        {
            return Fallback(port, [.. problems, $"无法加载证书 {path}：{exception.Message}"]);
        }
    }

    private static ListenEndpoint Fallback(int port, List<string> problems) =>
        new()
        {
            Address  = IPAddress.Loopback,
            Port     = port,
            Problems = problems
        };
}
