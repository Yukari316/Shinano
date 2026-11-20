using System.Globalization;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Plugin.ImageSources;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Plugin.Hso;

/// <summary>
/// 在群里发送图库图片：先发图，作品信息已确认时再发作品信息卡。是否允许 NSFW 由抽图时决定（见 <see cref="HsoCommands" />），这里不再判断。
/// 原图以预签名链接发送：20 MB 以内作为图片，超过时 QQ 不能作为图片发送，改为上传群文件。
/// 图片被拒（腾讯按内容摘要拦截）时改动一个像素另存为临时对象重发，成功则标记该图，之后每次发送都直接改像素；图库对象本身不替换。
/// 临时对象保留到每小时一次的 <see cref="CleanupAsync" />，供宿主归档经同一链接读取；未发出的立即删除。
/// </summary>
public sealed class HsoSender
{
    private const long   MIB           = 1024 * 1024;
    private const long   IMAGE_LIMIT   = 20 * MIB;
    private const string TEMP_PREFIX   = "tmp/";
    private const string BASE64_PREFIX = "base64://";
    private const string CARD_LABEL    = "HSO";
    private const string UNTITLED      = "无题";

    /// <summary>临时对象的保留时间：足够宿主归档读取，超过即由清理任务删除。</summary>
    public static readonly TimeSpan TempRetention = TimeSpan.FromHours(1);

    private readonly IPluginInitializationContext _context;
    private readonly HsoGallery                   _gallery;
    private readonly ILogger<HsoSender>           _logger;

    /// <summary>创建发送器。</summary>
    public HsoSender(IPluginInitializationContext context, HsoGallery gallery, ILogger<HsoSender> logger)
    {
        _context = context;
        _gallery = gallery;
        _logger  = logger;
    }

    /// <summary>发送图片，成功后在作品信息已确认时发送信息卡（待补全、待审核与查无信息时不发）。</summary>
    /// <returns>图片是否发出；信息卡失败只记录日志。</returns>
    public async Task<bool> SendAsync(MessageReceivedEvent e, HsoImageRecord record, CancellationToken cancellationToken)
    {
        bool sent = await SendOriginalAsync(e, record, cancellationToken);
        if (sent && record.InfoState == (int)HsoInfoState.Found && Card(record) is { } card) await SendCardAsync(e, card);
        return sent;
    }

    /// <summary>删除超过保留时间的临时对象。</summary>
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        IPluginBucket bucket = _gallery.Bucket;
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - TempRetention;
        int deleted = await bucket.DeleteOlderThanAsync(TEMP_PREFIX, cutoff, cancellationToken);
        _logger.LogInformation("Hso 临时对象清理（早于 {Cutoff}）：bucket {Bucket} 删除 {Count} 个对象", cutoff, bucket.Name, deleted);
    }

    /// <summary>作品信息卡：作者、UID（Pixiv 作者 ID）、PID 与 TAG（每个标签一个方框），没有的项不显示；没有任何作品信息时为 null。</summary>
    public static InfoCard? Card(HsoImageRecord record)
    {
        if (record.Title is null && record.Author is null && record.Pid is null) return null;

        IReadOnlyList<string> tags = record.TagList;
        return new InfoCard
        {
            Label = CARD_LABEL,
            Meta  = HsoGallery.SourceNameOf(record)?.ToUpperInvariant(),
            Title = record.Title ?? UNTITLED,
            Rows =
            [
                .. Row("作者", record.Author),
                .. Row("UID", record.AuthorId?.ToString(CultureInfo.InvariantCulture)),
                .. Row("PID", record.Pid?.ToString(CultureInfo.InvariantCulture)),
                .. tags.Count == 0
                    ? (CardRow[]) []
                    :
                    [
                        new CardRow
                        {
                            Label      = "TAG",
                            Style      = CardRowStyle.Items,
                            Items      = tags,
                            ItemBorder = true
                        }
                    ]
            ]
        };
    }

    /// <summary>卡片的文字形式，绘制失败时代替图片发送。</summary>
    public static string ToText(InfoCard card) =>
        string.Join('\n', [card.Title, .. card.Rows.Select(row => $"{row.Label}：{(row.Style == CardRowStyle.Items ? string.Join(' ', row.Items) : row.Text)}")]);

    private static CardRow[] Row(string label, string? text) =>
        text is null
            ? []
            :
            [
                new CardRow
                {
                    Label = label,
                    Text  = text
                }
            ];

    private async Task<bool> SendOriginalAsync(MessageReceivedEvent e, HsoImageRecord record, CancellationToken cancellationToken)
    {
        IPluginBucket bucket = _gallery.Bucket;
        if (record.Size > IMAGE_LIMIT)
        {
            ApiResult<string> upload = await e.Api.UploadGroupFileAsync(e.Message.GroupId, await bucket.CreateImUrlAsync(record.ObjectKey), FileName(record));
            if (!upload.IsSuccess) _logger.LogWarning("上传 Hso 图片 {Id} 为群文件失败：{Code} {Message}", record.Id, upload.Code, upload.Message);
            else _logger.LogInformation("Hso 图片 {Id} 超过 20 MB（{Size}），已作为群文件发送", record.Id, ImageInspector.FormatSize(record.Size));
            return upload.IsSuccess;
        }

        if (!record.Blocked && await SendImageAsync(e, await bucket.CreateImUrlAsync(record.ObjectKey), record.Id)) return true;

        bool sent = await SendTweakedAsync(e, record, cancellationToken);
        if (sent && !record.Blocked) await _gallery.MarkBlockedAsync(record);
        return sent;
    }

    // 改动一个像素另存为临时对象再发一次；图库对象不替换。
    private async Task<bool> SendTweakedAsync(MessageReceivedEvent e, HsoImageRecord record, CancellationToken cancellationToken)
    {
        if (await _gallery.ReadAsync(record, cancellationToken) is not { } original)
        {
            _logger.LogWarning("Hso 图片 {Id} 的对象 {Key} 不存在", record.Id, record.ObjectKey);
            return false;
        }

        if (PixelTweak.Apply(original, record.MediaType) is not { } tweaked)
        {
            _logger.LogInformation("Hso 图片 {Id}（{MediaType}）无法修改像素，不再重发", record.Id, record.MediaType);
            return false;
        }

        IPluginBucket bucket = _gallery.Bucket;
        string key = $"{TEMP_PREFIX}{Guid.NewGuid():N}{Path.GetExtension(record.ObjectKey)}";
        using (MemoryStream content = new(tweaked))
            await bucket.PutAsync(key, content, record.MediaType, null, cancellationToken);

        bool sent = await SendImageAsync(e, await bucket.CreateImUrlAsync(key), record.Id);
        _logger.LogInformation(
            sent ? "Hso 图片 {Id} 改像素后发送成功 {Before} → {After}" : "Hso 图片 {Id} 改像素后发送失败 {Before} → {After}",
            record.Id,
            record.Sha256,
            ImageInspector.Sha256(tweaked));
        if (sent)
        {
            _logger.LogInformation("临时对象 bucket {Bucket}/{Key} 保留供归档读取，{Hours} 小时后由清理任务删除", bucket.Name, key, TempRetention.TotalHours);
            return true;
        }

        await bucket.DeleteObjectAsync(key, CancellationToken.None);
        _logger.LogInformation("已从 bucket {Bucket} 删除未发出的临时对象 {Key}", bucket.Name, key);
        return false;
    }

    private async Task<bool> SendImageAsync(MessageReceivedEvent e, string url, long id)
    {
        SendMessageResult result = await e.ReplyAsync(new MessageBody().AddImage(url));
        if (!result.IsSuccess) _logger.LogWarning("发送 Hso 图片 {Id} 失败：{Code} {Error}", id, result.Code, result.ErrorMessage);
        return result.IsSuccess;
    }

    private async Task SendCardAsync(MessageReceivedEvent e, InfoCard card)
    {
        MessageBody body;
        try
        {
            body = new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(_context.Images.RenderInfo(card)));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制 Hso 作品信息卡失败，改发文字");
            body = new MessageBody(ToText(card));
        }

        SendMessageResult result = await e.ReplyAsync(body);
        if (!result.IsSuccess) _logger.LogWarning("发送 Hso 作品信息卡失败：{Code} {Error}", result.Code, result.ErrorMessage);
    }

    private static string FileName(HsoImageRecord record) =>
        record.FileName is { Length: > 0 } name ? Path.GetFileName(name) : $"hso-{record.Id}{Path.GetExtension(record.ObjectKey)}";
}
