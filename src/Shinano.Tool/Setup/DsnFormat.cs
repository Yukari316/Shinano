using Npgsql;
using Shinano.Tool.Cli;

namespace Shinano.Tool.Setup;

/// <summary>
/// 本地配置中的数据库 DSN，格式与宿主、监控相同：<c>postgres://</c>/<c>postgresql://</c> URI，或 Npgsql 键值对字符串。
/// </summary>
public static class DsnFormat
{
    private const int DEFAULT_PORT = 5432;

    private const string URI_EXTRAS_ERROR =
        "本地配置中的数据库 DSN 使用了 URI 写法的 ? 连接参数或 # 之后的内容，不支持。需要连接参数时改用键值对写法，"
        + "如 Host=db.example.com;Port=5432;Username=shinano;Password=密码;Database=shinano;SSL Mode=VerifyFull；"
        + "URI 写法中密码含 #、?、@、/、: 时需百分号编码，如 # 写作 %23";

    private static readonly string[] UriSchemes = ["postgres://", "postgresql://"];

    /// <summary>解析 DSN；为空时得到空的连接信息。</summary>
    /// <exception cref="ToolException">DSN 无法解析。</exception>
    public static NpgsqlConnectionStringBuilder Parse(string? dsn)
    {
        if (string.IsNullOrWhiteSpace(dsn)) return new NpgsqlConnectionStringBuilder();
        try
        {
            if (!UriSchemes.Any(scheme => dsn.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)))
                return new NpgsqlConnectionStringBuilder(dsn);

            Uri uri = new(dsn);
            // 查询参数与 # 之后的内容不会传给 Npgsql，与宿主一致地拒绝，而不是静默丢弃。
            if (uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new ToolException(URI_EXTRAS_ERROR);

            string[] userInfo = uri.UserInfo.Split(':', 2);
            return new NpgsqlConnectionStringBuilder
            {
                Host     = uri.Host,
                Port     = uri.IsDefaultPort || uri.Port <= 0 ? DEFAULT_PORT : uri.Port,
                Username = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
                Database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'))
            };
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException)
        {
            // 不回显 DSN 本身，其中含密码。
            throw new ToolException($"本地配置中的数据库 DSN 无法解析：{exception.GetType().Name}");
        }
    }

    /// <summary>连接所需的各项是否与 <paramref name="other" /> 相同；相同时保留原 DSN 文本，不改写格式。</summary>
    public static bool SameTarget(NpgsqlConnectionStringBuilder left, NpgsqlConnectionStringBuilder other) =>
        left.Host == other.Host
        && left.Port == other.Port
        && left.Database == other.Database
        && left.Username == other.Username
        && left.Password == other.Password;
}
