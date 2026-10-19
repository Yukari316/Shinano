using Npgsql;

namespace Shinano.Infrastructure.Database;

/// <summary>把配置中的 DSN 转换为 Npgsql 连接字符串。</summary>
public static class PostgresConnectionString
{
    private const int DEFAULT_PORT = 5432;

    /// <summary>
    /// 解析 DSN。会话时区固定为 UTC，使分区边界与时间值的文本形式不依赖服务器设置。
    /// </summary>
    /// <param name="dsn"><c>postgres://</c>/<c>postgresql://</c> URI，或 Npgsql 键值对字符串。</param>
    /// <exception cref="ArgumentException">DSN 为空或无法解析。</exception>
    public static NpgsqlConnectionStringBuilder Parse(string dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn)) throw new ArgumentException("数据库 DSN 未配置", nameof(dsn));

        NpgsqlConnectionStringBuilder builder = IsUri(dsn) ? FromUri(new Uri(dsn)) : new NpgsqlConnectionStringBuilder(dsn);
        builder.Timezone = "UTC";
        return builder;
    }

    private static bool IsUri(string dsn) =>
        dsn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
        || dsn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase);

    private static NpgsqlConnectionStringBuilder FromUri(Uri uri)
    {
        string[] userInfo = uri.UserInfo.Split(':', 2);
        string database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (userInfo[0].Length == 0 || database.Length == 0)
            throw new ArgumentException("DSN 缺少用户名或数据库名");

        return new NpgsqlConnectionStringBuilder
        {
            Host     = uri.Host,
            Port     = uri.IsDefaultPort || uri.Port <= 0 ? DEFAULT_PORT : uri.Port,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            Database = database
        };
    }
}
