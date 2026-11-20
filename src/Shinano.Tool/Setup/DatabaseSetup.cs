using Npgsql;
using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;

namespace Shinano.Tool.Setup;

/// <summary>
/// 数据库的前提核验，以及可选的管理员步骤：创建业务角色与数据库并授权。管理员凭据只在本次使用，不保存、不记录。
/// 数据库归管理员所有，业务角色获授 <c>CONNECT</c>、<c>CREATE</c>、<c>TEMP</c>，可以建 schema 但不能删库；不创建扩展。
/// </summary>
public static class DatabaseSetup
{
    /// <summary>支持的最低主版本（已在该版本上实测）。</summary>
    public const int MIN_MAJOR_VERSION = 18;

    private const string MAINTENANCE_DATABASE = "postgres";
    private const string REQUIRED_ENCODING    = "UTF8";
    private const int    VERSION_DIVISOR      = 10000;

    /// <summary>用管理员凭据核验服务器，并按需创建业务角色与数据库、授权。</summary>
    /// <param name="business">业务角色的连接信息（含要创建的数据库与角色）。</param>
    /// <param name="adminUser">管理员用户名。</param>
    /// <param name="adminPassword">管理员密码。</param>
    /// <param name="log">输出。</param>
    /// <exception cref="ToolException">连接失败、服务器不满足前提或执行失败。</exception>
    public static async Task ProvisionAsync(NpgsqlConnectionStringBuilder business, string adminUser, string adminPassword, ToolLog log)
    {
        NpgsqlConnectionStringBuilder admin = new(business.ConnectionString)
        {
            Database = MAINTENANCE_DATABASE,
            Username = adminUser,
            Password = adminPassword
        };
        string database = business.Database ?? throw new ToolException("缺少数据库名");
        string role = business.Username ?? throw new ToolException("缺少业务角色名");

        await using NpgsqlConnection connection = await OpenAsync(admin, "管理员");
        try
        {
            await ProvisionAsync(connection, database, role, business.Password ?? string.Empty, adminUser, log);
        }
        catch (NpgsqlException exception)
        {
            // 服务端消息不含执行的语句，不会带出密码。
            throw new ToolException($"以管理员 {adminUser} 开通数据库失败：{exception.Message}");
        }
    }

    private static async Task ProvisionAsync(NpgsqlConnection connection, string database, string role, string password, string adminUser, ToolLog log)
    {
        RequireServer(await ReadServerAsync(connection), checkEncoding: false);

        // 先确认权限再执行：没有权限时 CREATE/ALTER ROLE 会失败，PostgreSQL 会把失败的语句（含明文密码）写进服务端日志。
        if (!await ScalarAsync<bool>(connection, "select rolsuper or rolcreaterole from pg_roles where rolname = current_user"))
            throw new ToolException($"管理员 {adminUser} 没有创建角色的权限（需要 CREATEROLE 或超级用户）");

        // SQL_NEED: CREATE/ALTER ROLE 的密码不能参数化，由服务端的 format(%I, %L) 负责标识符与字面量的引号。
        bool roleExists = await ScalarAsync<bool>(connection, "select exists(select 1 from pg_roles where rolname = @name)", ("name", role));
        string roleSql = await ScalarAsync<string>(
            connection,
            roleExists ? "select format('alter role %I with login password %L', @name, @password)" : "select format('create role %I with login password %L', @name, @password)",
            ("name", role),
            ("password", password));
        await ExecuteAsync(connection, roleSql);
        log.Info(roleExists ? $"已更新业务角色 {role} 的密码" : $"已创建业务角色 {role}");

        string? encoding = await ScalarObjectAsync(
            connection,
            "select pg_encoding_to_char(encoding) from pg_database where datname = @name",
            ("name", database)) as string;
        if (encoding is null)
        {
            if (!await ScalarAsync<bool>(connection, "select rolsuper or rolcreatedb from pg_roles where rolname = current_user"))
                throw new ToolException($"数据库 {database} 不存在，管理员 {adminUser} 没有创建数据库的权限（需要 CREATEDB 或超级用户）");
            // SQL_NEED: CREATE DATABASE 不能在事务中执行，也不能参数化数据库名。
            await ExecuteAsync(
                connection,
                await ScalarAsync<string>(connection, "select format('create database %I encoding ''UTF8'' template template0', @name)", ("name", database)));
            log.Info($"已创建数据库 {database}（UTF8）");
        }
        else if (encoding != REQUIRED_ENCODING)
        {
            throw new ToolException($"数据库 {database} 已存在，但编码为 {encoding}，需要 {REQUIRED_ENCODING}");
        }

        // SQL_NEED: GRANT 的数据库名与角色名不能参数化。
        await ExecuteAsync(
            connection,
            await ScalarAsync<string>(
                connection,
                "select format('grant connect, create, temporary on database %I to %I', @database, @role)",
                ("database", database),
                ("role", role)));
        log.Info($"已授予业务角色 {role} 对数据库 {database} 的 CONNECT、CREATE、TEMP 权限");
    }

    /// <summary>用业务角色的连接串实际连接并核验。</summary>
    /// <returns>连接失败的原因；连接成功且满足前提时为 null。</returns>
    /// <exception cref="ToolException">账号是超级用户，或服务器、数据库不满足前提。</exception>
    public static async Task<string?> VerifyAsync(NpgsqlConnectionStringBuilder business)
    {
        NpgsqlConnection connection;
        try
        {
            connection = await OpenAsync(business, "业务角色");
        }
        catch (ToolException exception)
        {
            return exception.Message;
        }

        await using (connection)
        {
            if (await ScalarAsync<bool>(connection, "select rolsuper from pg_roles where rolname = current_user"))
                throw new ToolException($"{business.Username} 是 PostgreSQL 超级用户，Bot 不能使用超级用户运行；请改用业务角色");
            RequireServer(await ReadServerAsync(connection), checkEncoding: true);
            return null;
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(NpgsqlConnectionStringBuilder builder, string who)
    {
        NpgsqlConnection connection = new(builder.ConnectionString);
        try
        {
            await connection.OpenAsync();
            return connection;
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or TimeoutException)
        {
            await connection.DisposeAsync();
            // 只报告类别与服务端消息，不回显连接串。
            throw new ToolException($"无法以{who} {builder.Username} 连接 {builder.Host}:{builder.Port}/{builder.Database}：{exception.Message}");
        }
    }

    private static async Task<DatabaseServerInfo> ReadServerAsync(NpgsqlConnection connection) =>
        new()
        {
            VersionNumber = int.Parse(await ScalarAsync<string>(connection, "select current_setting('server_version_num')")),
            SupportsLz4   = await ScalarAsync<bool>(connection, "select 'lz4' = any(enumvals) from pg_settings where name = 'default_toast_compression'"),
            Encoding      = await ScalarAsync<string>(connection, "select pg_encoding_to_char(encoding) from pg_database where datname = current_database()")
        };

    private static void RequireServer(DatabaseServerInfo server, bool checkEncoding)
    {
        int major = server.VersionNumber / VERSION_DIVISOR;
        if (major < MIN_MAJOR_VERSION) throw new ToolException($"PostgreSQL 主版本为 {major}，需要 {MIN_MAJOR_VERSION} 或更高");
        if (!server.SupportsLz4) throw new ToolException("PostgreSQL 服务器不支持 lz4 列压缩（编译时未启用 lz4）");
        if (checkEncoding && server.Encoding != REQUIRED_ENCODING)
            throw new ToolException($"数据库编码为 {server.Encoding}，需要 {REQUIRED_ENCODING}");
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
        where T : notnull =>
        await ScalarObjectAsync(connection, sql, parameters) is T value
            ? value
            : throw new ToolException($"数据库查询没有返回预期的结果：{sql}");

    private static async Task<object?> ScalarObjectAsync(NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using NpgsqlCommand command = new(sql, connection);
        foreach ((string name, object value) in parameters)
            command.Parameters.AddWithValue(name, value);
        object? result = await command.ExecuteScalarAsync();
        return result is DBNull ? null : result;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

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
