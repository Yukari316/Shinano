using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Shinano.Host.Web;

/// <summary>
/// 把监听配置解析为安全的监听端点：拒绝全部网卡地址，非回环地址必须使用 HTTPS。
/// 配置不满足要求时回退到 <c>127.0.0.1</c> 的 HTTP，使面板仍可在本机打开，并给出原因。
/// </summary>
public static class WebEndpointResolver
{
    private const int    MIN_PORT  = 1;
    private const int    MAX_PORT  = 65535;
    private const string LOCALHOST = "localhost";

    /// <summary>解析监听端点。</summary>
    /// <param name="options">监听配置。</param>
    /// <param name="dataDirectory">部署数据目录，用于解析证书的相对路径。</param>
    public static WebEndpoint Resolve(WebOptions options, string dataDirectory)
    {
        List<string> problems = [];
        int port = options.Port;
        if (port is < MIN_PORT or > MAX_PORT)
        {
            problems.Add($"端口 {port} 超出范围，改用 {WebOptions.DEFAULT_PORT}");
            port = WebOptions.DEFAULT_PORT;
        }

        IPAddress? address = ParseAddress(options.ListenAddress);
        if (address is null)
            return Fallback(port, [.. problems, $"无法解析监听地址 \"{options.ListenAddress}\""]);
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return Fallback(port, [.. problems, $"不允许监听全部网卡地址 {address}"]);

        X509Certificate2? certificate = null;
        if (!string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            string path = Path.GetFullPath(options.CertificatePath, dataDirectory);
            certificate = LoadCertificate(path, options.CertificatePassword, out string? certificateProblem);
            if (certificateProblem is not null) problems.Add(certificateProblem);
        }

        if (!IPAddress.IsLoopback(address) && certificate is null)
            return Fallback(port, [.. problems, $"监听非回环地址 {address} 必须配置可用的 HTTPS 证书"]);

        return new WebEndpoint
        {
            Address     = address,
            Port        = port,
            Certificate = certificate,
            Problems    = problems
        };
    }

    private static IPAddress? ParseAddress(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Equals(LOCALHOST, StringComparison.OrdinalIgnoreCase)) return IPAddress.Loopback;
        return IPAddress.TryParse(trimmed, out IPAddress? address) ? address : null;
    }

    private static X509Certificate2? LoadCertificate(string path, string? password, out string? problem)
    {
        problem = null;
        if (!File.Exists(path))
        {
            problem = $"证书文件不存在：{path}";
            return null;
        }

        try
        {
            X509Certificate2 certificate = X509CertificateLoader.LoadPkcs12FromFile(path, password);
            if (certificate.HasPrivateKey) return certificate;

            certificate.Dispose();
            problem = $"证书 {path} 不含私钥";
            return null;
        }
        catch (CryptographicException exception)
        {
            problem = $"无法加载证书 {path}：{exception.Message}";
            return null;
        }
    }

    private static WebEndpoint Fallback(int port, List<string> problems) =>
        new()
        {
            Address  = IPAddress.Loopback,
            Port     = port,
            Problems = problems
        };
}
