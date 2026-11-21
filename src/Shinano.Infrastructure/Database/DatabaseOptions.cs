namespace Shinano.Infrastructure.Database;

/// <summary>数据库配置，对应配置节 <c>Shinano:Database</c>。DSN 含凭据，只写在部署配置文件中。</summary>
public sealed class DatabaseOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:Database";

    /// <summary>
    /// PostgreSQL 连接串：<c>postgres://用户:密码@主机:端口/数据库</c> 形式的 DSN，
    /// 或 Npgsql 的键值对连接字符串。
    /// </summary>
    public string Dsn { get; set; } = string.Empty;
}
