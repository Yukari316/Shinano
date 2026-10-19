using System.Collections.Concurrent;
using Npgsql;
using SqlSugar;

namespace Shinano.Infrastructure.Database;

/// <summary>
/// 宿主的数据库入口。每个 schema 使用独立的 <see cref="SqlSugarScope" />，
/// 连接的 <c>search_path</c> 指向该 schema，表名不带 schema 前缀。
/// </summary>
/// <remarks>
/// SqlSugar CodeFirst 对 <c>schema.table</c> 形式的表名不能正确判断表是否存在，
/// 因此不使用跨 schema 的限定名，而按 schema 区分连接。
/// </remarks>
public sealed class ShinanoDatabase : IDisposable
{
    /// <summary>要求的最低 PostgreSQL 主版本。</summary>
    public const int MINIMUM_SERVER_MAJOR_VERSION = 18;

    private const int SERVER_VERSION_DIVISOR = 10000;

    private readonly NpgsqlConnectionStringBuilder         _baseConnection;
    private readonly ConcurrentDictionary<long, SqlSugarScope> _accounts = new();

    /// <summary>创建数据库入口，不立即连接。</summary>
    /// <param name="baseConnection">不含 search_path 的连接设置。</param>
    /// <param name="schemas">schema 名称。</param>
    public ShinanoDatabase(NpgsqlConnectionStringBuilder baseConnection, SchemaNames schemas)
    {
        _baseConnection = baseConnection;
        Schemas         = schemas;
        Global          = CreateScope(SchemaNames.RequireIdentifier(schemas.Global));
    }

    /// <summary>schema 名称。</summary>
    public SchemaNames Schemas { get; }

    /// <summary>全局 schema 的客户端，线程安全。</summary>
    public SqlSugarScope Global { get; }

    /// <summary>返回机器人账号 schema 的客户端，线程安全，同一账号复用同一实例。</summary>
    public SqlSugarScope Account(long selfId) =>
        _accounts.GetOrAdd(selfId, id => CreateScope(SchemaNames.RequireIdentifier(Schemas.Account(id))));

    /// <summary>打开一个不设置 search_path 的原生连接，用于健康检查与 schema 创建。</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = new(_baseConnection.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// 检查连接并返回服务器主版本号。
    /// </summary>
    /// <exception cref="NpgsqlException">无法连接或认证失败。</exception>
    public async Task<int> GetServerMajorVersionAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand    command    = new("show server_version_num", connection);
        string versionNum = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? "0";
        return int.Parse(versionNum) / SERVER_VERSION_DIVISOR;
    }

    /// <summary>创建不存在的 schema。</summary>
    public async Task EnsureSchemaAsync(string schema, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenConnectionAsync(cancellationToken);
        await using NpgsqlCommand command = new(
            $"create schema if not exists {SchemaNames.RequireIdentifier(schema)}",
            connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Global.Dispose();
        foreach (SqlSugarScope scope in _accounts.Values)
            scope.Dispose();
    }

    private SqlSugarScope CreateScope(string schema)
    {
        NpgsqlConnectionStringBuilder connection = new(_baseConnection.ConnectionString)
        {
            SearchPath = schema
        };
        return new SqlSugarScope(
            new ConnectionConfig
            {
                ConnectionString      = connection.ConnectionString,
                DbType                = DbType.PostgreSQL,
                IsAutoCloseConnection = true,
                InitKeyType           = InitKeyType.Attribute
            });
    }
}
