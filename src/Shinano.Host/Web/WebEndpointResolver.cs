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

        if (ParseListenAddress(options.ListenAddress, out string? addressProblem) is not { } address)
            return Fallback(port, [.. problems, addressProblem ?? string.Empty]);

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

    /// <summary>
    /// 解析监听地址（面板与状态页共用）：接受具体 IP 或 <c>localhost</c>；无法解析或为全部网卡地址时返回 null，
    /// <paramref name="problem" /> 为可直接展示的原因。
    /// </summary>
    public static IPAddress? ParseListenAddress(string value, out string? problem)
    {
        string trimmed = value.Trim();
        IPAddress? address = trimmed.Equals(LOCALHOST, StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.TryParse(trimmed, out IPAddress? parsed) ? parsed : null;
        problem = address switch
        {
            null => $"无法解析监听地址 \"{value}\"",
            _ when address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) => $"不允许监听全部网卡地址 {address}",
            _ => null
        };
        return problem is null ? address : null;
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
