using Microsoft.Extensions.Logging;
using Shinano.Plugin.Gemini.Api;
using SkiaSharp;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>
/// 读取一条 Gemini 提问消息的文本与图片输入（含引用消息中的文字与图片，未内嵌时经 <c>get_message</c> 拉取）。
/// 单条消息最多 5 张图片，合计不超过 <see cref="GeminiApiClient.MAX_INLINE_MEDIA_BYTES" />；
/// Gemini 只接受 PNG、JPEG、WEBP、HEIC、HEIF，其他格式（如 QQ 常见的 GIF 表情）取首帧转为 PNG。
/// </summary>
public sealed class GeminiInputReader : IDisposable
{
    /// <summary>单次对话最多附带的图片数。</summary>
    public const int MAX_IMAGES = 5;

    private const string PNG_MEDIA_TYPE  = "image/png";
    private const string HEIC_MEDIA_TYPE = "image/heic";
    private const string HEIF_MEDIA_TYPE = "image/heif";
    private const int    PNG_QUALITY     = 100;
    private const int    BUFFER_BYTES    = 81920;

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<GeminiInputReader> _logger;
    private readonly HttpClient                 _http = new() { Timeout = DownloadTimeout };

    /// <summary>创建输入读取器。</summary>
    public GeminiInputReader(ILogger<GeminiInputReader> logger) => _logger = logger;

    /// <summary>
    /// 解析当前消息与可选引用消息中的文字和图片，下载图片并校验张数与体积限制。
    /// </summary>
    public async Task<GeminiInputReadResult> ReadAsync(
        MessageReceivedEvent e,
        string               promptAfterPrefix,
        CancellationToken    cancellationToken)
    {
        List<ImageSegment> imageSegments = [.. e.Message.Body.OfType<ImageSegment>()];
        string quotedText = string.Empty;

        if (e.Message.Body.OfType<ReplySegment>().FirstOrDefault() is { } reply)
        {
            MessageBody? quotedBody = await ResolveQuotedBodyAsync(e, reply);
            if (quotedBody is not null)
            {
                quotedText = quotedBody.GetText().Trim();
                imageSegments.AddRange(quotedBody.OfType<ImageSegment>());
            }
        }

        string prompt = BuildPromptText(promptAfterPrefix, quotedText);
        if (prompt.Length == 0 && imageSegments.Count == 0)
        {
            return new GeminiInputReadResult
            {
                Status = GeminiInputReadStatus.Empty
            };
        }

        if (imageSegments.Count > MAX_IMAGES)
        {
            return new GeminiInputReadResult
            {
                Status       = GeminiInputReadStatus.Failed,
                ErrorMessage = ReplyTexts.TOO_MANY_IMAGES
            };
        }

        List<GeminiMediaPart> downloaded = new(imageSegments.Count);
        long remainingBytes = GeminiApiClient.MAX_INLINE_MEDIA_BYTES;
        foreach (ImageSegment segment in imageSegments)
        {
            (GeminiMediaPart? media, string? error) = await DownloadImageAsync(segment, remainingBytes, cancellationToken);
            if (media is null)
            {
                return new GeminiInputReadResult
                {
                    Status       = GeminiInputReadStatus.Failed,
                    ErrorMessage = error ?? ReplyTexts.IMAGE_DOWNLOAD_FAILED
                };
            }

            downloaded.Add(media);
            remainingBytes -= media.Data.Length;
        }

        return new GeminiInputReadResult
        {
            Status = GeminiInputReadStatus.Ready,
            Prompt = prompt,
            Images = downloaded
        };
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();

    private async Task<MessageBody?> ResolveQuotedBodyAsync(MessageReceivedEvent e, ReplySegment reply)
    {
        if (reply.Content is { } inlineContent) return inlineContent;

        long peer = e.Message.SourceType == MessageSourceType.Group ? (long)e.Message.GroupId : (long)e.Message.SenderId;
        ApiResult<MessageContext> result = await e.Api.GetMessageAsync(e.Message.SourceType, peer, reply.TargetId);
        if (result is { IsSuccess: true, Data: { } message }) return message.Body;

        _logger.LogWarning("无法读取引用的消息 {MessageId}：{Code} {Message}", reply.TargetId, result.Code, result.Message);
        return null;
    }

    private static string BuildPromptText(string promptAfterPrefix, string quotedText)
    {
        string userPrompt = promptAfterPrefix.Trim();
        if (quotedText.Length == 0) return userPrompt;
        if (userPrompt.Length == 0) return $"[引用消息]\n{quotedText}";
        return $"[引用消息]\n{quotedText}\n\n{userPrompt}";
    }

    private async Task<(GeminiMediaPart? Media, string? Error)> DownloadImageAsync(ImageSegment image, long maxBytes, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(image.Url))
        {
            _logger.LogWarning("Gemini 输入图片缺少下载地址");
            return (null, ReplyTexts.IMAGE_DOWNLOAD_FAILED);
        }

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(image.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("下载 Gemini 输入图片失败：HTTP {StatusCode}", (int)response.StatusCode);
                return (null, ReplyTexts.IMAGE_DOWNLOAD_FAILED);
            }

            if (response.Content.Headers.ContentLength > maxBytes)
                return (null, ReplyTexts.IMAGE_TOO_LARGE);

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[BUFFER_BYTES];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    return (null, ReplyTexts.IMAGE_TOO_LARGE);

                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0) return (null, ReplyTexts.IMAGE_DOWNLOAD_FAILED);

            GeminiMediaPart? media = ToSupportedImage(buffer.ToArray(), response.Content.Headers.ContentType?.MediaType);
            if (media is null)
            {
                _logger.LogWarning("Gemini 输入图片无法识别或解码：Content-Type {ContentType}", response.Content.Headers.ContentType?.MediaType);
                return (null, ReplyTexts.IMAGE_DOWNLOAD_FAILED);
            }

            return media.Data.Length > maxBytes ? (null, ReplyTexts.IMAGE_TOO_LARGE) : (media, null);
        }
        // 读响应体时连接中断抛出的 HttpIOException 继承 IOException。
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("下载 Gemini 输入图片异常：{Message}", exception.Message);
            return (null, ReplyTexts.IMAGE_DOWNLOAD_FAILED);
        }
    }

    /// <summary>
    /// 按文件内容识别图片格式：Gemini 支持的格式原样返回，其他可解码的格式取首帧转为 PNG，无法识别时返回 null。
    /// SkiaSharp 通常不带 HEIF 解码器，无法识别时信任响应头声明的 HEIC/HEIF。
    /// </summary>
    internal static GeminiMediaPart? ToSupportedImage(byte[] content, string? declaredMediaType)
    {
        using SKData data = SKData.CreateCopy(content);
        using SKCodec? codec = SKCodec.Create(data);
        string? declared = declaredMediaType?.ToLowerInvariant();
        string? supported = codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Png  => PNG_MEDIA_TYPE,
            SKEncodedImageFormat.Jpeg => "image/jpeg",
            SKEncodedImageFormat.Webp => "image/webp",
            SKEncodedImageFormat.Heif => HEIF_MEDIA_TYPE,
            null                      => declared is HEIC_MEDIA_TYPE or HEIF_MEDIA_TYPE ? declared : null,
            _                         => null
        };
        if (supported is not null) return Media(supported, content);
        if (codec is null) return null;

        using SKBitmap? frame = SKBitmap.Decode(codec);
        using SKData? png = frame?.Encode(SKEncodedImageFormat.Png, PNG_QUALITY);
        return png is null ? null : Media(PNG_MEDIA_TYPE, png.ToArray());
    }

    private static GeminiMediaPart Media(string mediaType, byte[] data) =>
        new()
        {
            MediaType = mediaType,
            Data      = data
        };
}

/// <summary>输入读取状态。</summary>
public enum GeminiInputReadStatus
{
    /// <summary>无有效文字与图片（纯前缀静默忽略）。</summary>
    Empty,

    /// <summary>读取成功。</summary>
    Ready,

    /// <summary>超限或下载失败。</summary>
    Failed
}

/// <summary>一条消息的输入读取结果。</summary>
public sealed class GeminiInputReadResult
{
    /// <summary>读取状态。</summary>
    public required GeminiInputReadStatus Status { get; init; }

    /// <summary>拼装后的用户提问文本（可为空，当仅发送图片时）。</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>下载成功的图片列表。</summary>
    public IReadOnlyList<GeminiMediaPart> Images { get; init; } = [];

    /// <summary>失败时面向用户的提示文案。</summary>
    public string ErrorMessage { get; init; } = string.Empty;
}
