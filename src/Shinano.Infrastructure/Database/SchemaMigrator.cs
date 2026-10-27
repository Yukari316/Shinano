using Microsoft.Extensions.Logging;
using Shinano.Infrastructure.Database.Entities;
using Shinano.Infrastructure.Database.Models;
using SqlSugar;

namespace Shinano.Infrastructure.Database;

/// <summary>按版本顺序执行某个所有者在某个 schema 中尚未执行的显式迁移。</summary>
public sealed class SchemaMigrator
{
    /// <summary>宿主自身迁移的所有者名称。</summary>
    public const string HOST_OWNER = "host";

    private readonly ILogger<SchemaMigrator> _logger;

    /// <summary>创建迁移执行器。</summary>
    public SchemaMigrator(ILogger<SchemaMigrator> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 执行 <paramref name="migrations" /> 中版本高于已记录版本的迁移，每个迁移与版本记录在同一事务中提交。
    /// 某个迁移失败时回滚该迁移并抛出异常，已成功的迁移保留。
    /// </summary>
    /// <param name="client">目标 schema 的客户端。</param>
    /// <param name="owner">迁移所有者。</param>
    /// <param name="migrations">该所有者的全部迁移。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行后的版本号。</returns>
    /// <exception cref="ArgumentException">版本号重复或不是正数。</exception>
    public async Task<int> ApplyAsync(
        ISqlSugarClient                 client,
        string                          owner,
        IReadOnlyList<SchemaMigration> migrations,
        CancellationToken               cancellationToken)
    {
        if (migrations.Any(migration => migration.Version <= 0)
            || migrations.Select(migration => migration.Version).Distinct().Count() != migrations.Count)
            throw new ArgumentException($"{owner} 的迁移版本必须为互不相同的正数", nameof(migrations));

        client.CodeFirst.InitTables<SchemaVersionRecord>();
        SchemaVersionRecord? record = await client.Queryable<SchemaVersionRecord>()
                                                  .Where(item => item.Owner == owner)
                                                  .FirstAsync(cancellationToken);
        int current = record?.Version ?? 0;

        foreach (SchemaMigration migration in migrations.Where(item => item.Version > current).OrderBy(item => item.Version))
        {
            DbResult<bool> result = await client.Ado.UseTranAsync(async () =>
            {
                await migration.Apply(client, cancellationToken);
                await client.Storageable(
                                new SchemaVersionRecord
                                {
                                    Owner     = owner,
                                    Version   = migration.Version,
                                    AppliedAt = DateTimeOffset.UtcNow
                                })
                            .ExecuteCommandAsync(cancellationToken);
                return true;
            });
            if (!result.IsSuccess)
                throw new InvalidOperationException($"{owner} 的结构迁移 {migration.Version} 失败", result.ErrorException);

            current = migration.Version;
            _logger.LogInformation("{Owner} 的结构迁移已执行到版本 {Version}", owner, current);
        }

        return current;
    }
}
