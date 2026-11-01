using Microsoft.Extensions.Logging;
using Shinano.Plugin.ImageSearch.Models;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.ImageSearch;

/// <summary>
/// 取得要搜索的图片：指令消息本身的第一张图，或引用消息中的第一张图（引用未内嵌正文时经 get_message 获取）；
/// 下载 QQ 图片交给搜索服务上传，不把 QQ 临时链接交给海外的搜索服务。
/// </summary>
public sealed class QueryImageReader : IDisposable
{
    /// <summary>搜索图片的大小上限（trace.moe 的上限；SauceNAO 与 IQDB 更小，超出时由图源服务拒绝）。</summary>
    public const long MAX_BYTES = 25L * 1024 * 1024;

    private const string DEFAULT_MEDIA_TYPE = "image/jpeg";
    private const int    BUFFER_BYTES       = 81920;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<QueryImageReader> _logger;
    private readonly HttpClient                _http = new() { Timeout = Timeout };

    /// <summary>创建读取器。</summary>
    public QueryImageReader(ILogger<QueryImageReader> logger) => _logger = logger;

    /// <summary>指令消息或其引用消息中的第一张图；都没有时返回 null。</summary>
    public async Task<ImageSegment?> FindAsync(MessageReceivedEvent e)
    {
        if (FirstImage(e.Message.Body) is { } image) return image;
        if (e.Message.Body.OfType<ReplySegment>().FirstOrDefault() is not { } reply) return null;
        if (reply.Content is { } content) return FirstImage(content);

        long peer = e.Message.SourceType == MessageSourceType.Group ? (long)e.Message.GroupId : (long)e.Message.SenderId;
        ApiResult<MessageContext> original = await e.Api.GetMessageAsync(e.Message.SourceType, peer, reply.TargetId);
        if (original is { IsSuccess: true, Data: { } message }) return FirstImage(message.Body);

        _logger.LogWarning("无法获取引用的消息 {MessageId}：{Code} {Message}", reply.TargetId, original.Code, original.Message);
        return null;
    }

    /// <summary>消息中是否有图片，用于等待发图。</summary>
    public static bool HasImage(MessageReceivedEvent e) => FirstImage(e.Message.Body) is not null;

    /// <summary>下载图片；地址缺失、下载失败或超过 <see cref="MAX_BYTES" /> 时返回 null 并记录原因。</summary>
    public async Task<QueryImage?> DownloadAsync(ImageSegment image, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(image.Url))
        {
            _logger.LogWarning("搜图的图片没有下载地址");
            return null;
        }

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(image.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MAX_BYTES)
            {
                _logger.LogWarning(
                    "下载搜图的图片失败：HTTP {Status}，{Length}",
                    (int)response.StatusCode,
                    ImageInspector.FormatSize(response.Content.Headers.ContentLength));
                return null;
            }

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[BUFFER_BYTES];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MAX_BYTES)
                {
                    _logger.LogWarning("搜图的图片超过 {Limit}，放弃", ImageInspector.FormatSize(MAX_BYTES));
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }

            QueryImage query = new()
            {
                Content   = buffer.ToArray(),
                MediaType = response.Content.Headers.ContentType?.MediaType is { } type && type.StartsWith("image/", StringComparison.Ordinal) ? type : DEFAULT_MEDIA_TYPE
            };
            _logger.LogInformation(
                "收到要搜索的图片：{MediaType}，{Info}，{Size}，SHA256 {Sha256}",
                query.MediaType,
                ImageInspector.Describe(query.Content),
                ImageInspector.FormatSize(query.Content.Length),
                ImageInspector.Sha256(query.Content));
            return query;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("下载搜图的图片失败：{Error}", exception.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private static ImageSegment? FirstImage(MessageBody body) => body.OfType<ImageSegment>().FirstOrDefault();
}
