using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Storage;
using SqlSugar;

namespace Shinano.Infrastructure.Media;

/// <summary>
/// 持久媒体的唯一管理者：按内容去重保存，按引用记录决定生命周期。
/// 插件只增删自己的引用；最后一条引用删除后经宽限期由 <see cref="CollectOrphansAsync" /> 物理删除。
/// </summary>
/// <remarks>
/// 保存与回收都对媒体行加行锁，回收在删除前再次确认没有引用，保证不会删除刚被引用的对象。
/// </remarks>
public sealed class PersistentMediaStore
{
    private const int COLLECT_BATCH_SIZE = 500;

    private readonly ShinanoDatabase               _database;
    private readonly ObjectStorage                 _storage;
    private readonly string                        _bucket;
    private readonly TimeSpan                      _orphanGrace;
    private readonly ILogger<PersistentMediaStore> _logger;

    /// <summary>创建存储。</summary>
    /// <param name="database">数据库入口，表位于全局 schema。</param>
    /// <param name="storage">对象存储。</param>
    /// <param name="bucket">持久媒体 bucket 名。</param>
    /// <param name="options">媒体配置。</param>
    /// <param name="logger">日志。</param>
    public PersistentMediaStore(
        ShinanoDatabase               database,
        ObjectStorage                 storage,
        string                        bucket,
        MediaOptions                  options,
        ILogger<PersistentMediaStore> logger)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(options.OrphanGraceHours);
        _database    = database;
        _storage     = storage;
        _bucket      = bucket;
        _orphanGrace = TimeSpan.FromHours(options.OrphanGraceHours);
        _logger      = logger;
    }

    private SqlSugarScope Db => _database.Global;

    /// <summary>
    /// 保存媒体并登记引用。相同内容已存在时复用已有媒体，不再上传。
    /// </summary>
    /// <returns>媒体 ID。</returns>
    public async Task<long> StoreAsync(
        BufferedMedia     media,
        string            mediaType,
        MediaReference    reference,
        CancellationToken cancellationToken)
    {
        long? existing = await TryReferenceExistingAsync(media.Sha256, reference);
        if (existing is not null) return existing.Value;

        string key = $"{media.Sha256}-{Guid.NewGuid():N}";
        await _storage.PutFileAsync(_bucket, key, media.FilePath, mediaType, cancellationToken);
        StoredKey stored;
        try
        {
            stored = await Db.InTransactionAsync(async () =>
            {
                // SQL_NEED: 并发保存同一内容时需要单条语句完成"插入或返回已有行"，SqlSugar 不支持 on conflict ... returning。
                StoredKey row = await Db.Ado.SqlQuerySingleAsync<StoredKey>(
                    """
                    insert into media_object (sha256, size, media_type, object_key, orphaned_at, created_at)
                    values (@sha256, @size, @mediaType, @objectKey, null, @now)
                    on conflict (sha256) do update set orphaned_at = null
                    returning id as "Id", object_key as "ObjectKey"
                    """,
                    new
                    {
                        sha256    = media.Sha256,
                        size      = media.Length,
                        mediaType,
                        objectKey = key,
                        now       = DateTimeOffset.UtcNow
                    });
                await InsertReferenceAsync(row.Id, reference);
                return row;
            });
        }
        catch
        {
            await DeleteObjectQuietlyAsync(key);
            throw;
        }

        // 并发保存同一内容时另一方先写入，本次上传的对象多余。
        if (stored.ObjectKey != key) await DeleteObjectQuietlyAsync(key);
        return stored.Id;
    }

    /// <summary>为已有媒体登记引用。</summary>
    /// <returns>媒体存在并已登记时为 true；媒体不存在（已被回收）时为 false。</returns>
    public async Task<bool> AddReferenceAsync(long mediaId, MediaReference reference) =>
        await Db.InTransactionAsync(async () =>
        {
            long? id = await LockMediaAsync(item => item.Id == mediaId);
            if (id is null) return false;
            await InsertReferenceAsync(id.Value, reference);
            return true;
        });

    /// <summary>删除插件某个业务键下的全部引用，无引用的媒体进入宽限期。</summary>
    public async Task RemoveReferencesAsync(string pluginId, string refKey) =>
        await RemoveWhereAsync(reference => reference.PluginId == pluginId && reference.RefKey == refKey);

    /// <summary>删除插件的全部引用，用于管理员清除插件数据。</summary>
    public async Task RemovePluginReferencesAsync(string pluginId) =>
        await RemoveWhereAsync(reference => reference.PluginId == pluginId);

    /// <summary>读取媒体内容；媒体或对象不存在时返回 null。</summary>
    public async Task<StoredObject?> OpenReadAsync(long mediaId, CancellationToken cancellationToken)
    {
        string? key = await FindKeyAsync(mediaId);
        return key is null ? null : await _storage.OpenReadAsync(_bucket, key, cancellationToken);
    }

    /// <summary>生成 IM 实现端使用的预签名链接；媒体不存在时返回 null。</summary>
    public async Task<string?> CreateImUrlAsync(long mediaId)
    {
        string? key = await FindKeyAsync(mediaId);
        return key is null ? null : await _storage.CreateImUrlAsync(_bucket, key);
    }

    /// <summary>
    /// 删除宽限期已过且仍无引用的媒体：先删除数据库行，再删除对象。对象删除失败只记录警告。
    /// </summary>
    /// <returns>删除的媒体数。</returns>
    public async Task<int> CollectOrphansAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset cutoff = now - _orphanGrace;
        // SQL_NEED: 需要 for update skip locked 跳过正被引用而锁定的行，并以 returning 取回待删除对象；SqlSugar 不支持。
        List<string> keys = await Db.InTransactionAsync(async () =>
            await Db.Ado.SqlQueryAsync<string>(
                """
                delete from media_object
                where id in (
                    select m.id from media_object m
                    where m.orphaned_at < @cutoff
                      and not exists (select 1 from media_ref r where r.media_id = m.id)
                    order by m.orphaned_at
                    limit @limit
                    for update skip locked)
                returning object_key
                """,
                new
                {
                    cutoff,
                    limit = COLLECT_BATCH_SIZE
                }));

        foreach (string key in keys)
            await DeleteObjectQuietlyAsync(key);
        if (keys.Count > 0) _logger.LogInformation("已回收 {Count} 个无引用的持久媒体", keys.Count);
        cancellationToken.ThrowIfCancellationRequested();
        return keys.Count;
    }

    private async Task<long?> TryReferenceExistingAsync(string sha256, MediaReference reference) =>
        await Db.InTransactionAsync(async () =>
        {
            long? id = await LockMediaAsync(item => item.Sha256 == sha256);
            if (id is null) return null;
            await InsertReferenceAsync(id.Value, reference);
            return id;
        });

    // 清除宽限期标记的更新同时锁定媒体行，回收跳过被锁定的行；调用方必须处于事务中。
    private async Task<long?> LockMediaAsync(Expression<Func<MediaObjectRecord, bool>> condition)
    {
        int locked = await Db.Updateable<MediaObjectRecord>()
                             .SetColumns(item => new MediaObjectRecord { OrphanedAt = null })
                             .Where(condition)
                             .ExecuteCommandAsync();
        if (locked == 0) return null;

        return await Db.Queryable<MediaObjectRecord>().Where(condition).Select(item => item.Id).FirstAsync();
    }

    private async Task InsertReferenceAsync(long mediaId, MediaReference reference) =>
        // SQL_NEED: 并发登记同一引用时依赖 on conflict do nothing 保持幂等，SqlSugar 不支持。
        await Db.Ado.ExecuteCommandAsync(
            """
            insert into media_ref (media_id, plugin_id, ref_key, self_id, created_at)
            values (@mediaId, @pluginId, @refKey, @selfId, @now)
            on conflict do nothing
            """,
            new
            {
                mediaId,
                pluginId = reference.PluginId,
                refKey   = reference.RefKey,
                selfId   = reference.SelfId,
                now      = DateTimeOffset.UtcNow
            });

    // 删除引用后，只有确实不再被任何引用指向的媒体进入宽限期。
    private async Task RemoveWhereAsync(Expression<Func<MediaReferenceRecord, bool>> condition) =>
        await Db.InTransactionAsync(async () =>
        {
            List<long> mediaIds = await Db.Queryable<MediaReferenceRecord>()
                                          .Where(condition)
                                          .Select(reference => reference.MediaId)
                                          .Distinct()
                                          .ToListAsync();
            if (mediaIds.Count == 0) return 0;

            await Db.Deleteable<MediaReferenceRecord>().Where(condition).ExecuteCommandAsync();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return await Db.Updateable<MediaObjectRecord>()
                           .SetColumns(item => new MediaObjectRecord { OrphanedAt = now })
                           .Where(item => mediaIds.Contains(item.Id)
                                          && item.OrphanedAt == null
                                          && !SqlFunc.Subqueryable<MediaReferenceRecord>()
                                                     .Where(reference => reference.MediaId == item.Id)
                                                     .Any())
                           .ExecuteCommandAsync();
        });

    private async Task<string?> FindKeyAsync(long mediaId) =>
        await Db.Queryable<MediaObjectRecord>()
                .Where(item => item.Id == mediaId)
                .Select(item => item.ObjectKey)
                .FirstAsync();

    private async Task DeleteObjectQuietlyAsync(string key)
    {
        try
        {
            await _storage.DeleteAsync(_bucket, key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "删除持久媒体对象 {ObjectKey} 失败", key);
        }
    }

    private sealed class StoredKey
    {
        public long Id { get; set; }

        public string ObjectKey { get; set; } = string.Empty;
    }
}
