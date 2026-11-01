namespace Shinano.Infrastructure.Storage.Models;

/// <summary>RustFS（S3 兼容）配置，对应配置节 <c>Shinano:RustFs</c>。密钥只写在本地配置文件中。</summary>
public sealed class RustFsOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:RustFs";

    /// <summary>Bot 访问 RustFS 的地址，如 <c>http://10.7.21.12:9000</c>。</summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>IM 实现端下载媒体使用的地址，留空时与 <see cref="Endpoint" /> 相同。</summary>
    public string? ImEndpoint { get; set; }

    /// <summary>访问密钥 ID。</summary>
    public string AccessKey { get; set; } = string.Empty;

    /// <summary>访问密钥。</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>发给 IM 实现端的预签名链接有效期，单位分钟，默认 10。</summary>
    public int PresignExpiryMinutes { get; set; } = 10;
}
