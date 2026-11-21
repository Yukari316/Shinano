namespace Shinano.Host.Web;

/// <summary>
/// 管理面板监听配置，对应配置节 <c>Shinano:Web</c>，修改后重启生效。
/// 证书密码属于敏感配置，只写在部署配置文件中。
/// </summary>
public sealed class WebOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Web";

    /// <summary>默认监听端口。</summary>
    public const int DEFAULT_PORT = 19100;

    /// <summary>监听地址：IP 字面量或 <c>localhost</c>，默认 <c>127.0.0.1</c>。不接受全部网卡地址。</summary>
    public string ListenAddress { get; set; } = "127.0.0.1";

    /// <summary>监听端口，默认 <see cref="DEFAULT_PORT" />。</summary>
    public int Port { get; set; } = DEFAULT_PORT;

    /// <summary>PFX 证书路径，相对路径基于部署数据目录；留空时使用 HTTP。</summary>
    public string? CertificatePath { get; set; }

    /// <summary>PFX 证书密码，没有密码时留空。</summary>
    public string? CertificatePassword { get; set; }
}
