namespace Shinano.Tool.Setup.Models;

/// <summary>连接 PostgreSQL 后核验到的服务器与数据库信息。</summary>
public sealed class DatabaseServerInfo
{
    /// <summary><c>server_version_num</c>，如 180006。</summary>
    public required int VersionNumber { get; init; }

    /// <summary>服务器是否支持 lz4 列压缩。</summary>
    public required bool SupportsLz4 { get; init; }

    /// <summary>当前数据库的编码。</summary>
    public required string Encoding { get; init; }
}
