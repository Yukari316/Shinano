using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Framework.Plugins.Models;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Database.Models;
using SqlSugar;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>插件的数据库访问，迁移以插件 ID 为所有者记录版本。</summary>
internal sealed class PluginDatabase : IPluginDatabase
{
    private readonly string          _pluginId;
    private readonly ShinanoDatabase _database;
    private readonly IBotAccessor    _bot;
    private readonly SchemaMigrator  _migrator;

    /// <summary>创建访问入口。</summary>
    public PluginDatabase(string pluginId, ShinanoDatabase database, IBotAccessor bot, ILoggerFactory loggerFactory)
    {
        _pluginId = pluginId;
        _database = database;
        _bot      = bot;
        _migrator = new SchemaMigrator(loggerFactory.CreateLogger<SchemaMigrator>());
    }

    /// <inheritdoc />
    public ISqlSugarClient Global => _database.Global;

    /// <inheritdoc />
    public ISqlSugarClient Account => _database.Account(SelfId);

    /// <inheritdoc />
    public long SelfId =>
        _bot.SelfId is > 0 and long selfId ? selfId : throw new InvalidOperationException("机器人账号尚未连接");

    /// <inheritdoc />
    public Task MigrateGlobalAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) =>
        _migrator.ApplyAsync(Global, _pluginId, Map(migrations), cancellationToken);

    /// <inheritdoc />
    public Task MigrateAccountAsync(IReadOnlyList<PluginMigration> migrations, CancellationToken cancellationToken) =>
        _migrator.ApplyAsync(Account, _pluginId, Map(migrations), cancellationToken);

    private static List<SchemaMigration> Map(IReadOnlyList<PluginMigration> migrations) =>
    [
        .. migrations.Select(migration => new SchemaMigration
        {
            Version = migration.Version,
            Apply   = migration.Apply
        })
    ];
}
