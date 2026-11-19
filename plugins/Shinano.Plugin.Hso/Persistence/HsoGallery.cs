using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Gallery;
using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Panel;
using Shinano.Plugin.ImageSources;
using SqlSugar;

namespace Shinano.Plugin.Hso.Persistence;

/// <summary>
/// 图库：全局 schema 的 <c>hso_image</c> 表与插件 bucket（用途 <see cref="BUCKET_PURPOSE" />）中的对象。
/// 对象 key 由内容的 SHA256 决定，同一内容只存一份；图库中的对象不被替换，被拦截的图片在发送时临时改像素。线程安全。
/// </summary>
public sealed class HsoGallery
{
    /// <summary>插件 bucket 的用途，与 plugin.json 的 bucketPurposes 一致。</summary>
    public const string BUCKET_PURPOSE = "hso";

    /// <summary>图库对象的 key 前缀；<c>Shinano.Tool hso-import</c> 使用同一约定。</summary>
    public const string IMAGE_PREFIX = "images/";

    private const int    KEYWORD_LIMIT = 100;
    private const string PIXIV         = "Pixiv";

    private readonly IPluginInitializationContext _context;
    private readonly TimeProvider                 _time;
    private readonly ILogger<HsoGallery>          _logger;

    /// <summary>创建图库。</summary>
    public HsoGallery(IPluginInitializationContext context, TimeProvider time, ILogger<HsoGallery> logger)
    {
        _context = context;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>插件 bucket。</summary>
    public IPluginBucket Bucket => _context.Buckets.Get(BUCKET_PURPOSE);

    private ISqlSugarClient Database => _context.Database.Global;

    /// <summary>创建缺失的表与 bucket。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        Database.CodeFirst.InitTables<HsoImageRecord, HsoReviewRecord>();
        await Bucket.EnsureCreatedAsync(cancellationToken);
    }

    /// <summary>最早导入的若干张待补全图片的 ID，供后台任务补全。</summary>
    public async Task<List<long>> PendingIdsAsync(int count) =>
        await Database.Queryable<HsoImageRecord>()
                      .Where(record => record.InfoState == (int)HsoInfoState.Pending)
                      .OrderBy(record => record.ImportedAt)
                      .OrderBy(record => record.Id)
                      .Take(count)
                      .Select(record => record.Id)
                      .ToListAsync();

    /// <summary>内容 SHA256 为 <paramref name="sha256" /> 的其他图片的 ID；没有时为 null。</summary>
    public async Task<long?> FindOtherBySha256Async(string sha256, long excludeId) =>
        await Database.Queryable<HsoImageRecord>()
                      .Where(record => record.Sha256 == sha256 && record.Id != excludeId)
                      .Select(record => (long?)record.Id)
                      .FirstAsync();

    /// <summary>随机取一张图片；没有符合条件的图片时返回 null。</summary>
    /// <param name="allowNsfw">为 false 时只取当前未标为 NSFW 的图片（待补全的图片默认非 NSFW，补全后才能确定）。</param>
    /// <param name="resolvedOnly">只取不是待补全的图片（已确认、查无信息或待审核），它们的 NSFW 已按作品信息确定。</param>
    public async Task<HsoImageRecord?> PickAsync(bool allowNsfw, bool resolvedOnly) =>
        await Database.Queryable<HsoImageRecord>()
                      .WhereIF(!allowNsfw, record => !record.Nsfw)
                      .WhereIF(resolvedOnly, record => record.InfoState != (int)HsoInfoState.Pending)
                      .OrderBy(record => SqlFunc.GetRandom())
                      .FirstAsync();

    /// <summary>按 ID 取图片。</summary>
    public async Task<HsoImageRecord?> FindAsync(long id) =>
        await Database.Queryable<HsoImageRecord>().InSingleAsync(id);

    /// <summary>是否已有该 Pixiv 作品的这一页。</summary>
    public Task<bool> HasPixivPageAsync(long pid, int page) =>
        Database.Queryable<HsoImageRecord>().AnyAsync(record => record.Pid == pid && record.Page == page);

    /// <summary>
    /// 导入一张图片：识别格式，按 SHA256 去重后写入 bucket 与图库表。作品信息留待被抽中时补全。
    /// </summary>
    public async Task<HsoImportResult> ImportAsync(
        byte[]            content,
        string?           fileName,
        HsoImageOrigin    origin,
        long?             pid,
        int?              page,
        CancellationToken cancellationToken)
    {
        if (HsoImageFiles.Detect(content) is not { } format)
            return HsoImportResult.Of(fileName, HsoImportOutcome.Unsupported);

        string sha256 = ImageInspector.Sha256(content);
        if (await Database.Queryable<HsoImageRecord>().AnyAsync(record => record.Sha256 == sha256))
            return HsoImportResult.Of(fileName, HsoImportOutcome.Duplicate);

        string key = IMAGE_PREFIX + sha256 + format.Extension;
        try
        {
            using MemoryStream stream = new(content);
            await Bucket.PutAsync(key, stream, format.MediaType, null, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 超过部署的单文件上限或 RustFS 不可用。
            _logger.LogWarning(exception, "Hso 图片写入 bucket {Bucket}/{Key} 失败：{Size}", Bucket.Name, key, ImageInspector.FormatSize(content.LongLength));
            return HsoImportResult.Of(fileName, HsoImportOutcome.Failed);
        }

        HsoImageRecord record = new()
        {
            ObjectKey  = key,
            Sha256     = sha256,
            MediaType  = format.MediaType,
            Size       = content.LongLength,
            FileName   = fileName,
            Pid        = pid,
            Page       = page,
            InfoState  = (int)HsoInfoState.Pending,
            Origin     = (int)origin,
            ImportedAt = _time.GetUtcNow()
        };
        record.Id = await Database.Insertable(record).ExecuteReturnBigIdentityAsync();
        _logger.LogInformation(
            "Hso 图库导入 {Id}（{Origin}）：bucket {Bucket}/{Key}，{MediaType}，{Size}，pid {Pid} 页 {Page}，文件名 {FileName}",
            record.Id,
            origin,
            Bucket.Name,
            key,
            format.MediaType,
            ImageInspector.FormatSize(content.LongLength),
            pid,
            page,
            fileName);
        return new HsoImportResult
        {
            FileName = fileName,
            Outcome  = HsoImportOutcome.Imported,
            Id       = record.Id
        };
    }

    /// <summary>记录补全的结果并直接确认（查到时为已确认，查无结果时为查无信息）；查询失败时不改动。管理员手动设置过的 NSFW 不被覆盖。</summary>
    public async Task ApplyLookupAsync(HsoImageRecord record, HsoLookup lookup)
    {
        switch (lookup)
        {
            case { Status: HsoLookupStatus.Failed }:
                return;
            case { Status: HsoLookupStatus.Found, Info: { } info }:
                await SaveInfoAsync(record, info, HsoInfoState.Found);
                return;
            default:
                record.InfoState = (int)HsoInfoState.NotFound;
                await UpdateInfoAsync(record);
                return;
        }
    }

    /// <summary>
    /// 把查到的作品信息写入记录并设为 <paramref name="state" />（已确认或待审核）。记录没有 pid 时采用查到的 pid 与页号；
    /// 管理员手动设置过的 NSFW 不被覆盖。
    /// </summary>
    public Task SaveInfoAsync(HsoImageRecord record, HsoWorkInfo info, HsoInfoState state)
    {
        record.SourceName = info.SourceName;
        record.Pid        ??= info.Pid;
        record.Page       ??= info.Page;
        record.Title      = info.Title;
        record.Author     = info.Author;
        record.AuthorId   = info.AuthorId;
        record.TagList    = info.Tags;
        if (!record.NsfwManual) record.Nsfw = info.Nsfw;
        record.InfoState = (int)state;
        return UpdateInfoAsync(record);
    }

    /// <summary>写入记录的作品信息各列（含 pid 与页号）、NSFW 及其是否手动设置与补全状态。</summary>
    public async Task UpdateInfoAsync(HsoImageRecord record)
    {
        await Database.Updateable(record)
                      .UpdateColumns(item => new
                      {
                          item.SourceName,
                          item.Pid,
                          item.Page,
                          item.Title,
                          item.Author,
                          item.AuthorId,
                          item.Tags,
                          item.Nsfw,
                          item.NsfwManual,
                          item.InfoState
                      })
                      .ExecuteCommandAsync();
        _logger.LogInformation(
            "Hso 图片 {Id} 的作品信息：{State}，来源 {Source}，pid {Pid} 页 {Page}，NSFW {Nsfw}",
            record.Id,
            (HsoInfoState)record.InfoState,
            record.SourceName,
            record.Pid,
            record.Page,
            record.Nsfw);
    }

    /// <summary>以新内容替换图片的对象（审核中「覆盖」）：更新 key、SHA256、格式与大小并清除被拦截标记，作品信息设为已确认。</summary>
    public async Task ReplaceContentAsync(HsoImageRecord record, string objectKey, string sha256, string mediaType, long size)
    {
        record.ObjectKey = objectKey;
        record.Sha256    = sha256;
        record.MediaType = mediaType;
        record.Size      = size;
        record.Blocked   = false;
        record.InfoState = (int)HsoInfoState.Found;
        await Database.Updateable(record)
                      .UpdateColumns(item => new
                      {
                          item.ObjectKey,
                          item.Sha256,
                          item.MediaType,
                          item.Size,
                          item.Blocked,
                          item.InfoState
                      })
                      .ExecuteCommandAsync();
    }

    /// <summary>标记为被拦截，之后发送时直接改像素。</summary>
    public async Task MarkBlockedAsync(HsoImageRecord record)
    {
        record.Blocked = true;
        await Database.Updateable(record).UpdateColumns(item => new { item.Blocked }).ExecuteCommandAsync();
        _logger.LogInformation("Hso 图片 {Id} 被拦截，已标记，之后发送前先改像素", record.Id);
    }

    /// <summary>按条件分页列出图片，越新导入的越靠前。</summary>
    public async Task<HsoImagePage> QueryAsync(HsoImageQuery query)
    {
        ISugarQueryable<HsoImageRecord> queryable = Database.Queryable<HsoImageRecord>()
                                                            .WhereIF(query.Nsfw is not null, record => record.Nsfw == query.Nsfw)
                                                            .WhereIF(query.HasPid == true, record => record.Pid != null)
                                                            .WhereIF(query.HasPid == false, record => record.Pid == null)
                                                            .WhereIF(query.Blocked is not null, record => record.Blocked == query.Blocked);
        if (query.Keyword?.Trim() is { Length: > 0 } keyword)
        {
            string text = keyword.Length > KEYWORD_LIMIT ? keyword[..KEYWORD_LIMIT] : keyword;
            queryable = long.TryParse(text, out long pid)
                ? queryable.Where(record => record.Pid == pid || record.FileName!.Contains(text))
                : queryable.Where(record => record.Title!.Contains(text) || record.Author!.Contains(text) || record.FileName!.Contains(text));
        }

        RefAsync<int> total = 0;
        List<HsoImageRecord> records = await queryable.OrderByDescending(record => record.ImportedAt)
                                                      .OrderByDescending(record => record.Id)
                                                      .ToPageListAsync(query.Page, HsoImageQuery.PAGE_SIZE, total);
        return new HsoImagePage
        {
            Total = total.Value,
            Items = [.. records.Select(HsoImageView.From)]
        };
    }

    /// <summary>管理员设置 NSFW，之后补全作品信息不再覆盖。</summary>
    /// <returns>图片不存在时为 false。</returns>
    public async Task<bool> SetNsfwAsync(long id, bool nsfw)
    {
        int updated = await Database.Updateable<HsoImageRecord>()
                                    .SetColumns(record => new HsoImageRecord
                                    {
                                        Nsfw       = nsfw,
                                        NsfwManual = true
                                    })
                                    .Where(record => record.Id == id)
                                    .ExecuteCommandAsync();
        if (updated > 0) _logger.LogInformation("面板设置 Hso 图片 {Id} 的 NSFW 为 {Nsfw}", id, nsfw);
        return updated > 0;
    }

    /// <summary>清除被拦截标记。</summary>
    /// <returns>图片不存在时为 false。</returns>
    public async Task<bool> ClearBlockedAsync(long id)
    {
        int updated = await Database.Updateable<HsoImageRecord>()
                                    .SetColumns(record => record.Blocked == false)
                                    .Where(record => record.Id == id)
                                    .ExecuteCommandAsync();
        if (updated > 0) _logger.LogInformation("面板清除 Hso 图片 {Id} 的被拦截标记", id);
        return updated > 0;
    }

    /// <summary>删除图片的对象与记录；有待审核记录时一并删除它与暂存的来源原图。</summary>
    /// <returns>图片不存在时为 false。</returns>
    public async Task<bool> DeleteAsync(long id, CancellationToken cancellationToken)
    {
        if (await FindAsync(id) is not { } record) return false;

        if (await Database.Queryable<HsoReviewRecord>().InSingleAsync(id) is { } review)
        {
            if (review.ObjectKey is { } candidate) await Bucket.DeleteObjectAsync(candidate, cancellationToken);
            await Database.Deleteable<HsoReviewRecord>().In(id).ExecuteCommandAsync();
        }

        await Bucket.DeleteObjectAsync(record.ObjectKey, cancellationToken);
        await Database.Deleteable<HsoImageRecord>().In(id).ExecuteCommandAsync();
        _logger.LogInformation("面板删除 Hso 图片 {Id}：已删除 bucket {Bucket}/{Key} 与图库记录", id, Bucket.Name, record.ObjectKey);
        return true;
    }

    /// <summary>读取图片内容。</summary>
    /// <returns>对象不存在时为 null。</returns>
    public async Task<byte[]?> ReadAsync(HsoImageRecord record, CancellationToken cancellationToken)
    {
        await using Stream? stream = await Bucket.OpenReadAsync(record.ObjectKey, cancellationToken);
        if (stream is null) return null;
        using MemoryStream buffer = new();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>作品信息卡的来源名：有 pid 时为 Pixiv。</summary>
    public static string? SourceNameOf(HsoImageRecord record) => record.SourceName ?? (record.Pid is not null ? PIXIV : null);
}

/// <summary>面板图库的筛选与分页。</summary>
public sealed class HsoImageQuery
{
    /// <summary>每页张数。</summary>
    public const int PAGE_SIZE = 20;

    /// <summary>页码，从 1 开始。</summary>
    public int Page { get; init; } = 1;

    /// <summary>只看 NSFW 或非 NSFW；为 null 时不筛选。</summary>
    public bool? Nsfw { get; init; }

    /// <summary>只看有或没有 pid 的；为 null 时不筛选。</summary>
    public bool? HasPid { get; init; }

    /// <summary>只看被拦截过或没有被拦截的；为 null 时不筛选。</summary>
    public bool? Blocked { get; init; }

    /// <summary>关键字：数字匹配 pid，其余匹配标题、作者与文件名；为空时不筛选。</summary>
    public string? Keyword { get; init; }
}
