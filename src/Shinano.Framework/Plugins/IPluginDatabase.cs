using SqlSugar;

namespace Shinano.Framework.Plugins;

/// <summary>
/// 插件的数据库访问。表名必须以插件标识为前缀；与账号无关的数据放在全局 schema，属于机器人账号的数据放在账号 schema。
/// </summary>
public interface IPluginDatabase
{
    /// <summary>全局 schema 的客户端，线程安全。</summary>
    ISqlSugarClient Global { get; }

    /// <summary>当前机器人账号 schema 的客户端，线程安全。</summary>
    ISqlSugarClient Account { get; }

    /// <summary>当前机器人账号。</summary>
    long SelfId { get; }

    /// <summary>
    /// 在全局 schema 中按版本执行本插件尚未执行的迁移。失败时抛出异常，插件保持停用。
    /// </summary>
    Task MigrateGlobalAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken);

    /// <summary>
    /// 在当前账号 schema 中按版本执行本插件尚未执行的迁移。失败时抛出异常，插件保持停用。
    /// </summary>
    Task MigrateAccountAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken);
}

/// <summary>
/// 插件的显式破坏性结构迁移（删列、改类型、改名等）。新增表与可空列使用 CodeFirst，不需要迁移。
/// </summary>
public sealed class PluginMigration
{
    /// <summary>迁移版本，同一插件同一 schema 内从 1 开始严格递增。</summary>
    public required int Version { get; init; }

    /// <summary>迁移内容，在事务中执行；客户端的 search_path 已指向目标 schema。</summary>
    public required Func<ISqlSugarClient, CancellationToken, Task> Apply { get; init; }
}
