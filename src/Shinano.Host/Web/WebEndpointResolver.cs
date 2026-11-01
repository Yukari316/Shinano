using Shinano.Host.Web.Models;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Shinano.Host.Web;

/// <summary>
/// 把监听配置解析为监听端点：拒绝全部网卡地址；HTTPS 可选，配置了证书时使用证书。
/// 配置无效，或配置了证书却无法加载时，回退到 <c>127.0.0.1</c> 的 HTTP 并给出原因：
/// 管理员要求 HTTPS 时不能因证书问题改为以明文对外监听，回环地址上面板仍可在本机打开。
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
            if (certificateProblem is not null) return Fallback(port, [.. problems, certificateProblem]);
        }

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
