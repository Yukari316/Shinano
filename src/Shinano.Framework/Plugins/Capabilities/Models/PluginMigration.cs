using SqlSugar;

namespace Shinano.Framework.Plugins.Capabilities.Models;

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
