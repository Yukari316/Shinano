using Microsoft.Extensions.Logging;
using Shinano.Infrastructure.Database;
using Shinano.Infrastructure.Media.Entities;
using Shinano.Infrastructure.Media.Models;
using Shinano.Infrastructure.Storage;
using Shinano.Infrastructure.Storage.Models;
using SqlSugar;

namespace Shinano.Infrastructure.Media;

/// <summary>
/// 聊天媒体存储：同一部署内相同内容复用，每次被新消息引用时刷新 <c>last_referenced_at</c>，
/// 超过聊天记录保留期未刷新的对象由 <see cref="ExpireAsync" /> 删除。
/// </summary>
public sealed class ChatMediaStore
{
    private const int EXPIRE_BATCH_SIZE = 500;

    private readonly ShinanoDatabase         _database;
    private readonly ObjectStorage           _storage;
    private readonly string                  _bucket;
    private readonly ILogger<ChatMediaStore> _logger;

    /// <summary>创建存储。</summary>
    /// <param name="database">数据库入口，表位于全局 schema。</param>
    /// <param name="storage">对象存储。</param>
    /// <param name="bucket">聊天媒体 bucket 名。</param>
    /// <param name="logger">日志。</param>
    public ChatMediaStore(ShinanoDatabase database, ObjectStorage storage, string bucket, ILogger<ChatMediaStore> logger)
    {
        _database = database;
        _storage  = storage;
        _bucket   = bucket;
        _logger   = logger;
    }

    private SqlSugarScope Db => _database.Global;

    /// <summary>保存聊天媒体或刷新已有媒体的引用时间。</summary>
    /// <returns>聊天媒体 ID。</returns>
    public async Task<long> StoreAsync(BufferedMedia media, string mediaType, CancellationToken cancellationToken)
    {
        // 刷新引用时间的更新同时锁定该行，过期清理跳过被锁定的行，因此不会删除正被复用的媒体。
        DateTimeOffset now = DateTimeOffset.UtcNow;
        long? existing = await Db.InTransactionAsync(async () =>
        {
            int refreshed = await Db.Updateable<ChatMediaObjectRecord>()
                                    .SetColumns(item => new ChatMediaObjectRecord { LastReferencedAt = now })
                                    .Where(item => item.Sha256 == media.Sha256)
                                    .ExecuteCommandAsync(cancellationToken);
            if (refreshed == 0) return (long?)null;

            return await Db.Queryable<ChatMediaObjectRecord>()
                           .Where(item => item.Sha256 == media.Sha256)
                           .Select(item => item.Id)
                           .FirstAsync(cancellationToken);
        });
        if (existing is not null) return existing.Value;

        string key = $"{media.Sha256}-{Guid.NewGuid():N}";
        await _storage.PutFileAsync(_bucket, key, media.FilePath, mediaType, cancellationToken);
        StoredKey stored;
        try
        {
            // SQL_NEED: 并发保存同一内容时需要单条语句完成"插入或返回已有行"，SqlSugar 不支持 on conflict ... returning。
            stored = await Db.Ado.SqlQuerySingleAsync<StoredKey>(
                """
                insert into chat_media_object (sha256, size, media_type, object_key, last_referenced_at, created_at)
                values (@sha256, @size, @mediaType, @objectKey, @now, @now)
                on conflict (sha256) do update set last_referenced_at = excluded.last_referenced_at
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
        }
        catch
        {
            await DeleteObjectQuietlyAsync(key);
            throw;
        }

        if (stored.ObjectKey != key) await DeleteObjectQuietlyAsync(key);
        return stored.Id;
    }

    /// <summary>读取聊天媒体；媒体或对象不存在时返回 null。</summary>
    public async Task<StoredObject?> OpenReadAsync(long chatMediaId, CancellationToken cancellationToken)
    {
        string? key = await FindKeyAsync(chatMediaId);
        return key is null ? null : await _storage.OpenReadAsync(_bucket, key, cancellationToken);
    }

    /// <summary>生成 IM 实现端使用的预签名链接；媒体不存在时返回 null。</summary>
    public async Task<string?> CreateImUrlAsync(long chatMediaId)
    {
        string? key = await FindKeyAsync(chatMediaId);
        return key is null ? null : await _storage.CreateImUrlAsync(_bucket, key);
    }

    /// <summary>
    /// 删除 <paramref name="cutoff" /> 之前最后被引用的聊天媒体，分批执行直到没有过期项。
    /// </summary>
    /// <returns>删除的媒体数。</returns>
    public async Task<int> ExpireAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        int total = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            // SQL_NEED: 需要 for update skip locked 跳过正被复用而锁定的行，并以 returning 取回待删除对象；SqlSugar 不支持。
            List<string> keys = await Db.Ado.SqlQueryAsync<string>(
                """
                delete from chat_media_object
                where id in (
                    select id from chat_media_object
                    where last_referenced_at < @cutoff
                    order by last_referenced_at
                    limit @limit
                    for update skip locked)
                returning object_key
                """,
                new
                {
                    cutoff,
                    limit = EXPIRE_BATCH_SIZE
                });
            foreach (string key in keys)
                await DeleteObjectQuietlyAsync(key);
            total += keys.Count;
            if (keys.Count < EXPIRE_BATCH_SIZE) break;
        }

        if (total > 0) _logger.LogInformation("已清理 {Count} 个过期聊天媒体", total);
        return total;
    }

    private async Task<string?> FindKeyAsync(long chatMediaId) =>
        await Db.Queryable<ChatMediaObjectRecord>()
                .Where(item => item.Id == chatMediaId)
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
            _logger.LogWarning(exception, "删除聊天媒体对象 {ObjectKey} 失败", key);
        }
    }

    private sealed class StoredKey
    {
        public long Id { get; set; }

        public string ObjectKey { get; set; } = string.Empty;
    }
}
