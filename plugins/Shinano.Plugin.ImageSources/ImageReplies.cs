using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 搜图与 Hso 共用的发图辅助：信息卡（绘制失败改发文字）、超过 <see cref="IMAGE_LIMIT" /> 的图片改发文件、
/// 图片被拒后改一个像素重发。对象来自哪里、原对象是否保留由调用方决定。
/// </summary>
public static class ImageReplies
{
    /// <summary>作为图片发送的大小上限（20 MB）；超过时改发群文件或私聊文件。</summary>
    public const long IMAGE_LIMIT = 20 * 1024 * 1024;

    private const string BASE64_PREFIX = "base64://";

    /// <summary>以宿主信息卡回复；绘制失败时改发 <see cref="InfoCard.ToText" />。发送失败只记录日志。</summary>
    public static async Task SendCardAsync(MessageReceivedEvent e, IPluginImages images, InfoCard card, ILogger logger)
    {
        MessageBody body;
        try
        {
            body = new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(images.RenderInfo(card)));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "绘制信息卡 {Title} 失败，改发文字", card.Title);
            body = new MessageBody(card.ToText());
        }

        SendMessageResult result = await e.ReplyAsync(body);
        if (!result.IsSuccess) logger.LogWarning("发送信息卡 {Title} 失败：{Code} {Error}", card.Title, result.Code, result.ErrorMessage);
    }

    /// <summary>把 <paramref name="url" /> 上传为当前会话的文件：群聊为群文件，私聊为私聊文件。</summary>
    /// <returns>是否上传成功；失败只记录日志。</returns>
    public static async Task<bool> UploadFileAsync(MessageReceivedEvent e, string url, string fileName, ILogger logger)
    {
        ApiResult<string> result = e.Message.SourceType == MessageSourceType.Group
            ? await e.Api.UploadGroupFileAsync(e.Message.GroupId, url, fileName)
            : await e.Api.UploadPrivateFileAsync(e.Message.SenderId, url, fileName);
        if (!result.IsSuccess) logger.LogWarning("上传文件 {FileName} 失败：{Code} {Message}", fileName, result.Code, result.Message);
        return result.IsSuccess;
    }

    /// <summary>
    /// 图片被拒后调用：把 <paramref name="original" /> 改动一个像素写为 <paramref name="key" /> 并发送。发出时对象保留，
    /// 供宿主归档经同一链接读取，由调用方的清理任务删除；未发出时立即删除。格式无法修改像素时不写入、不发送。
    /// </summary>
    /// <returns>是否发出。</returns>
    public static async Task<bool> SendTweakedAsync(
        MessageReceivedEvent e,
        IPluginBucket        bucket,
        byte[]               original,
        string               mediaType,
        string               key,
        long?                limitBytes,
        ILogger              logger,
        CancellationToken    cancellationToken)
    {
        if (PixelTweak.Apply(original, mediaType) is not { } tweaked)
        {
            logger.LogInformation("{MediaType} 无法修改像素，不再重发", mediaType);
            return false;
        }

        using (MemoryStream content = new(tweaked))
            await bucket.PutAsync(key, content, mediaType, limitBytes, cancellationToken);
        SendMessageResult result = await e.ReplyAsync(new MessageBody().AddImage(await bucket.CreateImUrlAsync(key)));
        logger.LogInformation(
            result.IsSuccess ? "改像素后重发成功 {Before} → {After}" : "改像素后重发失败 {Before} → {After}",
            ImageInspector.Sha256(original),
            ImageInspector.Sha256(tweaked));
        if (result.IsSuccess)
        {
            logger.LogInformation("临时对象 bucket {Bucket}/{Key} 保留供归档读取，之后由清理任务删除", bucket.Name, key);
            return true;
        }

        logger.LogWarning("重发图片失败：{Code} {Error}", result.Code, result.ErrorMessage);
        await DeleteQuietlyAsync(bucket, key, logger);
        return false;
    }

    /// <summary>删除未发出的临时对象；失败只记录日志，由清理任务兜底。</summary>
    public static async Task DeleteQuietlyAsync(IPluginBucket bucket, string key, ILogger logger)
    {
        try
        {
            await bucket.DeleteObjectAsync(key, CancellationToken.None);
            logger.LogInformation("已从 bucket {Bucket} 删除未发出的临时对象 {Key}", bucket.Name, key);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "从 bucket {Bucket} 删除临时对象 {Key} 失败，留给清理任务", bucket.Name, key);
        }
    }
}
