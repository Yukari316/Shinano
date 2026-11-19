namespace Shinano.Monitor.Web;

/// <summary>一组 Web 监听配置：监控自身的 <c>Shinano:Monitor</c>，或主进程面板的 <c>Shinano:Web</c>。</summary>
public sealed class ListenOptions
{
    /// <summary>监听地址：IP 字面量或 <c>localhost</c>。</summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    /// <summary>端口。</summary>
    public int Port { get; set; }

    /// <summary>PFX 证书路径，相对路径基于部署数据目录；留空时使用 HTTP。</summary>
    public string? CertificatePath { get; set; }

    /// <summary>证书密码。</summary>
    public string? CertificatePassword { get; set; }
}
