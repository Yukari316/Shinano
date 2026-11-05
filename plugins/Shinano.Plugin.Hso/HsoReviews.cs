using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Entities;
using Shinano.Plugin.Hso.Models;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Models;
using SqlSugar;

namespace Shinano.Plugin.Hso;

/// <summary>
/// 作品信息审核队列：补全查到的来源原图与图库图片的 SHA256 不一致或原图下载失败时，查到的信息以待审核写入图片记录，
/// 来源原图暂存在插件 bucket 的 <see cref="CANDIDATE_PREFIX" /> 下，由管理员在面板覆盖、只确认信息或否决；处理后删除暂存对象与审核记录。
/// </summary>
public sealed class HsoReviews
{
    /// <summary>暂存来源原图的 key 前缀，处理审核后删除。</summary>
    public const string CANDIDATE_PREFIX = "reviews/";

    private readonly IPluginInitializationContext _context;
    private readonly HsoGallery                   _gallery;
    private readonly TimeProvider                 _time;
    private readonly ILogger<HsoReviews>          _logger;

    /// <summary>创建审核队列。</summary>
    public HsoReviews(IPluginInitializationContext context, HsoGallery gallery, TimeProvider time, ILogger<HsoReviews> logger)
    {
        _context = context;
        _gallery = gallery;
        _time    = time;
        _logger  = logger;
    }

    private ISqlSugarClient Database => _context.Database.Global;

    /// <summary>
    /// 放入审核队列：暂存来源原图（有时），写入审核记录，查到的作品信息以待审核写入图片记录。
    /// 暂存失败时按原图下载失败处理，仍进入队列。
    /// </summary>
    /// <param name="record">图片记录。</param>
    /// <param name="info">查到的作品信息。</param>
    /// <param name="candidate">来源原图内容与格式；下载失败时为 null。</param>
    /// <param name="fetchError">原图下载失败的原因；<paramref name="candidate" /> 不为 null 时忽略。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task EnqueueAsync(
        HsoImageRecord                                 record,
        HsoWorkInfo                                    info,
        (byte[] Content, HsoImageFormat Format)?       candidate,
        string?                                        fetchError,
        CancellationToken                              cancellationToken)
    {
        HsoReviewRecord review = new()
        {
            ImageId    = record.Id,
            Method     = (int)info.Method,
            Similarity = info.Similarity,
            WorkSite   = info.Work is { } work ? (int)work.Site : null,
            WorkId     = info.Work?.Id,
            WorkPage   = info.Work?.Page,
            FetchError = fetchError,
            CreatedAt  = _time.GetUtcNow()
        };
        if (candidate is ({ } content, { } format))
        {
            string sha256 = ImageInspector.Sha256(content);
            string key = $"{CANDIDATE_PREFIX}{record.Id}/{sha256}{format.Extension}";
            try
            {
                using MemoryStream stream = new(content);
                await _gallery.Bucket.PutAsync(key, stream, format.MediaType, null, cancellationToken);
                review.ObjectKey  = key;
                review.Sha256     = sha256;
                review.MediaType  = format.MediaType;
                review.Size       = content.LongLength;
                review.Width      = format.Width;
                review.Height     = format.Height;
                review.FetchError = null;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // 超过部署的单文件上限或 RustFS 不可用。
                _logger.LogWarning(exception, "暂存 Hso 图片 {Id} 的来源原图到 bucket {Bucket}/{Key} 失败", record.Id, _gallery.Bucket.Name, key);
                review.FetchError = "暂存原图失败";
            }
        }

        await Database.Storageable(review).ExecuteCommandAsync();
        await _gallery.SaveInfoAsync(record, info, HsoInfoState.Review);
        _logger.LogInformation(
            "Hso 图片 {Id} 进入作品信息审核队列：来源 {Site} {WorkId}，暂存原图 {Key}，{Reason}",
            record.Id,
            info.Work?.Site,
            info.Work?.Id,
            review.ObjectKey ?? "无",
            review.FetchError ?? "SHA256 与图库图片不一致");
    }

    /// <summary>队列中全部图片的 ID，先进入的在前。</summary>
    public async Task<List<long>> IdsAsync() =>
        await Database.Queryable<HsoReviewRecord>()
                      .OrderBy(review => review.CreatedAt)
                      .OrderBy(review => review.ImageId)
                      .Select(review => review.ImageId)
                      .ToListAsync();

    /// <summary>审核记录；不在队列中时为 null。</summary>
    public async Task<HsoReviewRecord?> FindAsync(long imageId) =>
        await Database.Queryable<HsoReviewRecord>().InSingleAsync(imageId);

    /// <summary>队列中一张图片的对比与作品信息；不在队列中时为 null。图库图片的尺寸读取对象后识别。</summary>
    public async Task<HsoReviewView?> DetailAsync(long imageId, CancellationToken cancellationToken)
    {
        if (await FindAsync(imageId) is not { } review || await _gallery.FindAsync(imageId) is not { } record) return null;

        HsoImageFormat? current = await _gallery.ReadAsync(record, cancellationToken) is { } content ? HsoImageFiles.Detect(content) : null;
        return new HsoReviewView
        {
            ImageId    = record.Id,
            FileName   = record.FileName,
            Origin     = (HsoImageOrigin)record.Origin,
            ImportedAt = record.ImportedAt,
            Current = new HsoReviewImage
            {
                MediaType = record.MediaType,
                Size      = record.Size,
                Width     = current?.Width,
                Height    = current?.Height,
                Sha256    = record.Sha256
            },
            Candidate = review is { ObjectKey: not null, Sha256: { } sha256, MediaType: { } mediaType, Size: { } size }
                ? new HsoReviewImage
                {
                    MediaType = mediaType,
                    Size      = size,
                    Width     = review.Width,
                    Height    = review.Height,
                    Sha256    = sha256
                }
                : null,
            FetchError = review.FetchError,
            Method     = (HsoLookupMethod)review.Method,
            Similarity = review.Similarity,
            WorkSite   = review.WorkSite is { } site ? ((ImageSite)site).ToString() : null,
            WorkId     = review.WorkId,
            WorkPage   = review.WorkPage,
            CreatedAt  = review.CreatedAt,
            SourceName = record.SourceName,
            Title      = record.Title,
            Author     = record.Author,
            AuthorId   = record.AuthorId,
            Pid        = record.Pid,
            Page       = record.Page,
            Tags       = record.TagList,
            Nsfw       = record.Nsfw
        };
    }

    /// <summary>覆盖：用暂存的来源原图替换图库对象，确认作品信息，清除被拦截标记并删除旧对象。</summary>
    public async Task<HsoReviewResult> ReplaceAsync(long imageId, CancellationToken cancellationToken)
    {
        if (await FindAsync(imageId) is not { } review || await _gallery.FindAsync(imageId) is not { } record)
            return HsoReviewResult.Of(HsoReviewOutcome.NotFound);
        if (review is not { ObjectKey: { } candidateKey, Sha256: { } sha256, MediaType: { } mediaType })
            return HsoReviewResult.Of(HsoReviewOutcome.NoCandidate);
        if (await _gallery.FindOtherBySha256Async(sha256, imageId) is { } duplicate)
            return new HsoReviewResult
            {
                Outcome     = HsoReviewOutcome.Duplicate,
                DuplicateId = duplicate
            };

        IPluginBucket bucket = _gallery.Bucket;
        string key = HsoGallery.IMAGE_PREFIX + sha256 + Path.GetExtension(candidateKey);
        long size;
        await using (Stream? candidate = await bucket.OpenReadAsync(candidateKey, cancellationToken))
        {
            if (candidate is null) return HsoReviewResult.Of(HsoReviewOutcome.NoCandidate);
            size = await bucket.PutAsync(key, candidate, mediaType, null, cancellationToken);
        }

        string oldKey = record.ObjectKey;
        string oldSha256 = record.Sha256;
        await _gallery.ReplaceContentAsync(record, key, sha256, mediaType, size);
        await bucket.DeleteObjectAsync(oldKey, cancellationToken);
        await FinishAsync(review, cancellationToken);
        _logger.LogInformation(
            "面板审核 Hso 图片 {Id}：覆盖为来源原图 {Before} → {After}（{Size}），已删除旧对象 {OldKey}",
            imageId,
            oldSha256,
            sha256,
            ImageInspector.FormatSize(size),
            oldKey);
        return HsoReviewResult.Of(HsoReviewOutcome.Done);
    }

    /// <summary>只确认信息：保留图库图片，确认查到的作品信息。</summary>
    public async Task<HsoReviewResult> AcceptAsync(long imageId, CancellationToken cancellationToken)
    {
        if (await FindAsync(imageId) is not { } review || await _gallery.FindAsync(imageId) is not { } record)
            return HsoReviewResult.Of(HsoReviewOutcome.NotFound);

        record.InfoState = (int)HsoInfoState.Found;
        await _gallery.UpdateInfoAsync(record);
        await FinishAsync(review, cancellationToken);
        _logger.LogInformation("面板审核 Hso 图片 {Id}：保留图库图片，确认作品信息", imageId);
        return HsoReviewResult.Of(HsoReviewOutcome.Done);
    }

    /// <summary>
    /// 否决：丢弃查到的作品信息，改用 <paramref name="manual" /> 中填写的信息；全部留空时记为查无信息。之后不再自动查询。
    /// NSFW 按表单设置：勾选时记为管理员手动设置，否则恢复为非 NSFW（管理员此前手动设置过的不变）。
    /// </summary>
    public async Task<HsoReviewResult> RejectAsync(long imageId, HsoManualInfo manual, CancellationToken cancellationToken)
    {
        if (await FindAsync(imageId) is not { } review || await _gallery.FindAsync(imageId) is not { } record)
            return HsoReviewResult.Of(HsoReviewOutcome.NotFound);

        // SauceNAO 补全时记录中的 pid 与页号来自查询结果，一并作废；按 pid 查询时 pid 来自文件名或导入，保留。
        if (review.Method == (int)HsoLookupMethod.SauceNao)
        {
            record.Pid  = null;
            record.Page = null;
        }

        if (manual.Pid is { } pid)
        {
            record.Pid  = pid;
            record.Page = manual.Page;
        }

        record.SourceName = null;
        record.Title      = Text(manual.Title);
        record.Author     = Text(manual.Author);
        record.AuthorId   = manual.AuthorId;
        record.TagList    = Tags(manual.Tags);
        if (manual.Nsfw)
        {
            record.Nsfw       = true;
            record.NsfwManual = true;
        }
        else if (!record.NsfwManual)
        {
            record.Nsfw = false;
        }

        record.InfoState = (int)(HasAnyInfo(manual) ? HsoInfoState.Found : HsoInfoState.NotFound);
        await _gallery.UpdateInfoAsync(record);
        await FinishAsync(review, cancellationToken);
        _logger.LogInformation("面板审核 Hso 图片 {Id}：否决查到的作品信息，{Result}", imageId, HasAnyInfo(manual) ? "改用手动补全的信息" : "记为查无信息");
        return HsoReviewResult.Of(HsoReviewOutcome.Done);
    }

    /// <summary>手动补全的表单是否填写了任何作品信息（NSFW 不算）。</summary>
    public static bool HasAnyInfo(HsoManualInfo manual) =>
        Text(manual.Title) is not null || Text(manual.Author) is not null || manual.AuthorId is not null || manual.Pid is not null || Tags(manual.Tags).Count > 0;

    private async Task FinishAsync(HsoReviewRecord review, CancellationToken cancellationToken)
    {
        if (review.ObjectKey is { } candidate) await _gallery.Bucket.DeleteObjectAsync(candidate, cancellationToken);
        await Database.Deleteable<HsoReviewRecord>().In(review.ImageId).ExecuteCommandAsync();
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string> Tags(IReadOnlyList<string> tags) =>
        [.. tags.Select(Text).OfType<string>().Distinct()];
}
