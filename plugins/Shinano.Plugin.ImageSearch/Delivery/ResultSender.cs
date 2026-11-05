using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Framework.Plugins.Models;
using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.ImageSearch.Models;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Models;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Plugin.ImageSearch.Delivery;

/// <summary>
/// 发送搜索结果：先发卡片，再另发原图或番剧预览，原图失败不影响卡片；R-18 结果以共享库中的替代图代替原图或预览。
/// 原图从图源服务流式转存到插件 bucket（下载期间每秒记录进度与速度），以预签名链接发送：20 MB 以内作为图片，超过 20 MB 时 QQ 不能作为图片发送，
/// 改为上传群文件或私聊文件。图片被拒（腾讯按内容摘要拦截）时改动一个像素重发一次。
/// 发送成功的对象保留到每小时一次的 <see cref="CleanupAsync" />：宿主归档 Bot 自己发出的消息时，会经同一链接下载图片；
/// 未发出的对象立即删除。
/// </summary>
public sealed class ResultSender : IDisposable
{
    /// <summary>插件 bucket 的用途，与 plugin.json 的 bucketPurposes 一致。</summary>
    public const string BUCKET_PURPOSE = "search";

    private const long   MIB              = 1024 * 1024;
    private const long   IMAGE_LIMIT      = 20 * MIB;
    private const string BASE64_PREFIX    = "base64://";
    private const string OCTET_STREAM     = "application/octet-stream";
    private const string FILE_NAME_PREFIX = "search";

    /// <summary>临时原图的保留时间：足够宿主归档读取，超过即由清理任务删除。</summary>
    public static readonly TimeSpan ObjectRetention = TimeSpan.FromHours(1);

    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromSeconds(15);

    private readonly IPluginInitializationContext _context;
    private readonly YukariGateway                _gateway;
    private readonly ImageResolver                _resolver;
    private readonly ILogger<ResultSender>        _logger;
    private readonly HttpClient                   _http = new() { Timeout = PreviewTimeout };
    private          bool                         _bucketReady;

    /// <summary>创建发送器。</summary>
    public ResultSender(IPluginInitializationContext context, YukariGateway gateway, ImageResolver resolver, ILogger<ResultSender> logger)
    {
        _context  = context;
        _gateway  = gateway;
        _resolver = resolver;
        _logger   = logger;
    }

    /// <summary>
    /// 发送插画结果：先查来源作品，再发卡片（来源为 Pixiv 时用 Pixiv 的作品信息，并附来源作品的标签），
    /// 之后下载原图发到同一会话；R-18 时以替代图代替原图，Pixiv 作品已删除时另行说明。
    /// 已知为成人内容的结果（如 IQDB 的 Explicit）不查来源作品，卡片只用搜索结果中的信息。
    /// </summary>
    public async Task SendIllustrationAsync(MessageReceivedEvent e, SearchHit hit, double threshold, bool lowSimilarity, CancellationToken cancellationToken)
    {
        ImageResolution resolution = hit.Adult ? ImageResolution.None : await _resolver.ResolveAsync(hit.Sources, cancellationToken);
        _logger.LogInformation(
            "搜图结果 {Source} 的来源作品：{Work}，原图 {Image}，R-18 {Adult}，已删除 {Deleted}，标签 {Tags} 个",
            hit.SourceName,
            resolution.Work is { } work ? $"{work.Site}:{work.Id}" : "无",
            resolution.Image is { } source ? $"{source.Site}:{source.Id}" : "无",
            hit.Adult || resolution.Adult,
            resolution.Deleted,
            resolution.Tags.Count);
        await SendCardAsync(e, SearchCards.Illustration(hit, resolution, threshold, lowSimilarity, _context.Config<ImageSearchConfig>().Value.MaxTags));
        if (hit.Adult || resolution.Adult) await SendAdultImageAsync(e);
        else if (resolution.Image is { } image) await SendOriginalAsync(e, image, cancellationToken);
        else if (resolution.Deleted) await e.ReplyAsync(new MessageBody(ReplyTexts.DELETED));
    }

    /// <summary>发送番剧结果：卡片，再附 trace.moe 的画面预览（成人番以替代图代替）；预览取不到时只记录日志。</summary>
    public async Task SendAnimeAsync(MessageReceivedEvent e, SearchHit hit, double threshold, bool lowSimilarity, CancellationToken cancellationToken)
    {
        await SendCardAsync(e, SearchCards.Anime(hit, threshold, lowSimilarity));
        if (hit.Adult)
        {
            await SendAdultImageAsync(e);
            return;
        }

        if (hit.PreviewUrl is not { } previewUrl) return;
        try
        {
            byte[] preview = await _http.GetByteArrayAsync(previewUrl, cancellationToken);
            await e.ReplyAsync(new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(preview)));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("取番剧预览失败：{Error}", exception.Message);
        }
    }

    /// <summary>删除 bucket 中超过 <see cref="ObjectRetention" /> 的临时原图。</summary>
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        IPluginBucket bucket = _context.Buckets.Get(BUCKET_PURPOSE);
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - ObjectRetention;
        int deleted = 0;
        await foreach (PluginBucketObject item in bucket.ListAsync(null, cancellationToken))
        {
            if (item.LastModified >= cutoff) continue;
            await bucket.DeleteObjectAsync(item.Key, cancellationToken);
            deleted++;
        }

        _logger.LogInformation("搜图临时原图清理（早于 {Cutoff}）：bucket {Bucket} 删除 {Count} 个对象", cutoff, bucket.Name, deleted);
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private async Task SendCardAsync(MessageReceivedEvent e, InfoCard card)
    {
        MessageBody body;
        try
        {
            body = new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(_context.Images.RenderInfo(card)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制搜图卡片失败，改发文字");
            body = new MessageBody(SearchCards.ToText(card));
        }

        SendMessageResult result = await e.ReplyAsync(body);
        if (!result.IsSuccess) _logger.LogWarning("发送搜图卡片失败：{Code} {Error}", result.Code, result.ErrorMessage);
    }

    private async Task SendAdultImageAsync(MessageReceivedEvent e)
    {
        SendMessageResult result = await e.ReplyAsync(new MessageBody().AddImage(AdultPlaceholder.ImageUri));
        if (!result.IsSuccess) _logger.LogWarning("发送 R-18 替代图失败：{Code} {Error}", result.Code, result.ErrorMessage);
    }

    private async Task SendOriginalAsync(MessageReceivedEvent e, SourceRef source, CancellationToken cancellationToken)
    {
        long limit = _context.Config<ImageSearchConfig>().Value.MaxOriginalMiB * MIB;
        using HttpResponseMessage? response = await _gateway.OpenOriginalAsync(source, cancellationToken);
        long? length = response?.Content.Headers.ContentLength;
        if (response is null || length > limit)
        {
            if (length > limit)
                _logger.LogWarning(
                    "{Site} 作品 {Id} 的原图 {Length}，超过上限 {Limit}",
                    source.Site,
                    source.Id,
                    ImageInspector.FormatSize(length),
                    ImageInspector.FormatSize(limit));
            await e.ReplyAsync(new MessageBody(ReplyTexts.SEND_FAILED));
            return;
        }

        string mediaType = response.Content.Headers.ContentType?.MediaType ?? OCTET_STREAM;
        string key = NewKey(mediaType);
        IPluginBucket bucket = _context.Buckets.Get(BUCKET_PURPOSE);
        long size = 0;
        bool sent = false;
        try
        {
            await EnsureBucketAsync(bucket, cancellationToken);
            string label = source.Page is { } page ? $" {source.Site} 作品 {source.Id} 第 {page} 页" : $" {source.Site} 作品 {source.Id}";
            await using (Stream content = new DownloadProgressStream(await response.Content.ReadAsStreamAsync(cancellationToken), length, label, _logger))
                size = await bucket.PutAsync(key, content, mediaType, limit, cancellationToken);
            _logger.LogInformation(
                "从图源服务取得 {Site} 作品 {Id} 的原图（声明 {Length}），已写入 bucket {Bucket}/{Key}：{MediaType}，{Size}",
                source.Site,
                source.Id,
                ImageInspector.FormatSize(length),
                bucket.Name,
                key,
                mediaType,
                ImageInspector.FormatSize(size));

            bool asFile = size > IMAGE_LIMIT;
            sent = asFile
                ? await UploadFileAsync(e, await bucket.CreateImUrlAsync(key), $"{FILE_NAME_PREFIX}-{source.Site.ToString().ToLowerInvariant()}-{source.Id}{Extension(mediaType)}")
                : await SendImageAsync(e, bucket, key);
            if (sent) LogRetained(bucket, key);
            else if (!asFile) sent = await ResendTweakedAsync(e, bucket, key, mediaType, limit, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "转存或发送 {Site} 作品 {Id} 的原图失败", source.Site, source.Id);
        }
        finally
        {
            if (!sent) await DeleteQuietlyAsync(bucket, key);
        }

        if (sent) return;
        _logger.LogWarning("{Site} 作品 {Id} 的原图未能发出：{MediaType}，{Size}", source.Site, source.Id, mediaType, ImageInspector.FormatSize(size));
        await e.ReplyAsync(new MessageBody(ReplyTexts.SEND_FAILED));
    }

    // 第一次发送被拒后调用：删除原对象，改动一个像素另存为新对象再发一次，仍失败就放弃。
    private async Task<bool> ResendTweakedAsync(
        MessageReceivedEvent e,
        IPluginBucket        bucket,
        string               key,
        string               mediaType,
        long                 limit,
        CancellationToken    cancellationToken)
    {
        byte[] original;
        await using (Stream? stored = await bucket.OpenReadAsync(key, cancellationToken))
        {
            if (stored is null) return false;
            using MemoryStream buffer = new();
            await stored.CopyToAsync(buffer, cancellationToken);
            original = buffer.ToArray();
        }

        await DeleteQuietlyAsync(bucket, key);
        _logger.LogInformation("原图被拒，修改随机像素后重发：{Info}，{Size}", ImageInspector.Describe(original), ImageInspector.FormatSize(original.Length));
        if (PixelTweak.Apply(original, mediaType) is not { } tweaked)
        {
            _logger.LogInformation("{MediaType} 无法修改像素，不再重发", mediaType);
            return false;
        }

        string retryKey = NewKey(mediaType);
        using (MemoryStream content = new(tweaked))
            await bucket.PutAsync(retryKey, content, mediaType, limit, cancellationToken);
        _logger.LogInformation("修改后的原图已写入 bucket {Bucket}/{Key}：{Size}", bucket.Name, retryKey, ImageInspector.FormatSize(tweaked.Length));

        bool sent = await SendImageAsync(e, bucket, retryKey);
        _logger.LogInformation(
            sent ? "图片重发成功 {Before} → {After}" : "图片重发失败 {Before} → {After}",
            ImageInspector.Sha256(original),
            ImageInspector.Sha256(tweaked));
        if (sent) LogRetained(bucket, retryKey);
        else await DeleteQuietlyAsync(bucket, retryKey);
        return sent;
    }

    private async Task<bool> SendImageAsync(MessageReceivedEvent e, IPluginBucket bucket, string key)
    {
        SendMessageResult result = await e.ReplyAsync(new MessageBody().AddImage(await bucket.CreateImUrlAsync(key)));
        if (!result.IsSuccess) _logger.LogWarning("发送原图失败：{Code} {Error}", result.Code, result.ErrorMessage);
        return result.IsSuccess;
    }

    private async Task<bool> UploadFileAsync(MessageReceivedEvent e, string url, string fileName)
    {
        ApiResult<string> result = e.Message.SourceType == MessageSourceType.Group
            ? await e.Api.UploadGroupFileAsync(e.Message.GroupId, url, fileName)
            : await e.Api.UploadPrivateFileAsync(e.Message.SenderId, url, fileName);
        if (!result.IsSuccess) _logger.LogWarning("上传原图文件失败：{Code} {Message}", result.Code, result.Message);
        return result.IsSuccess;
    }

    private async Task EnsureBucketAsync(IPluginBucket bucket, CancellationToken cancellationToken)
    {
        if (_bucketReady) return;
        await bucket.EnsureCreatedAsync(cancellationToken);
        _bucketReady = true;
    }

    private async Task DeleteQuietlyAsync(IPluginBucket bucket, string key)
    {
        try
        {
            await bucket.DeleteObjectAsync(key, CancellationToken.None);
            _logger.LogInformation("已从 bucket {Bucket} 删除未发出的原图 {Key}", bucket.Name, key);
        }
        catch (Exception exception)
        {
            _logger.LogWarning("删除搜图的临时原图 {Key} 失败：{Error}", key, exception.Message);
        }
    }

    private void LogRetained(IPluginBucket bucket, string key) =>
        _logger.LogInformation("原图已发出，bucket {Bucket}/{Key} 保留供归档读取，{Hours} 小时后由清理任务删除", bucket.Name, key, ObjectRetention.TotalHours);

    private static string NewKey(string mediaType) => $"{Guid.NewGuid():N}{Extension(mediaType)}";

    private static string Extension(string mediaType) =>
        mediaType switch
        {
            "image/jpeg" => ".jpg",
            "image/png"  => ".png",
            "image/gif"  => ".gif",
            "image/webp" => ".webp",
            _            => string.Empty
        };
}
