using System.Net;
using System.Net.Sockets;

namespace Shinano.Host.Panel.Models;

/// <summary>
/// 面板侧边栏的服务状态页链接，按 <c>Shinano:Monitor</c> 配置构造。监控是独立网站，默认值与其保持一致：
/// 监听 <c>127.0.0.1:19101</c>，配置了证书时使用 HTTPS；地址无效或为全部网卡地址时监控回退到回环地址。
/// </summary>
public sealed class StatusPageLink
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Monitor";

    private const int DEFAULT_PORT = 19101;

    /// <summary>状态页地址。</summary>
    public required string Url { get; init; }

    /// <summary>状态页是否只监听回环地址，即只能在部署机本机打开。</summary>
    public required bool LoopbackOnly { get; init; }

    /// <summary>按配置构造链接。</summary>
    public static StatusPageLink From(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(SECTION_NAME);
        string text = section["ListenAddress"] is { Length: > 0 } value ? value.Trim() : "127.0.0.1";
        IPAddress address = text.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.TryParse(text, out IPAddress? parsed) && !parsed.Equals(IPAddress.Any) && !parsed.Equals(IPAddress.IPv6Any)
                ? parsed
                : IPAddress.Loopback;
        int port = section.GetValue("Port", DEFAULT_PORT) is var configured and >= 1 and <= 65535 ? configured : DEFAULT_PORT;
        string scheme = string.IsNullOrWhiteSpace(section["CertificatePath"]) ? "http" : "https";
        string host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
        return new StatusPageLink
        {
            Url          = $"{scheme}://{host}:{port}/",
            LoopbackOnly = IPAddress.IsLoopback(address)
        };
    }
}
