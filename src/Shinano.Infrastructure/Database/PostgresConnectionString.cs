using Npgsql;

namespace Shinano.Infrastructure.Database;

/// <summary>把配置中的 DSN 转换为 Npgsql 连接字符串。</summary>
public static class PostgresConnectionString
{
    private const int DEFAULT_PORT = 5432;

    private const string URI_EXTRAS_ERROR =
        "DSN 的 URI 写法不支持 ? 之后的连接参数或 # 之后的内容。需要连接参数时改用键值对写法，"
        + "如 Host=db.example.com;Port=5432;Username=shinano;Password=密码;Database=shinano;SSL Mode=VerifyFull；"
        + "URI 写法中密码含 #、?、@、/、: 时需百分号编码，如 # 写作 %23";

    /// <summary>
    /// 解析 DSN。会话时区固定为 UTC，使分区边界与时间值的文本形式不依赖服务器设置。
    /// </summary>
    /// <param name="dsn"><c>postgres://</c>/<c>postgresql://</c> URI（不带查询参数），或 Npgsql 键值对字符串。</param>
    /// <exception cref="ArgumentException">DSN 为空或无法解析。</exception>
    /// <exception cref="NotSupportedException">URI 写法带查询参数或 # 片段；消息给出改写示例，不含 DSN 本身，可直接展示给管理员。</exception>
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
        // 查询参数（如 ?sslmode=verify-full）与 # 之后的内容不会传给 Npgsql，静默丢弃会让连接方式与管理员的预期不符。
        if (uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new NotSupportedException(URI_EXTRAS_ERROR);

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
