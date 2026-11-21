using Microsoft.Extensions.Logging;
using Shinano.Plugin.Gemini.Api;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.Gemini.Chat;

/// <summary>
/// 读取一条 Gemini 提问消息的文本与图片输入（含引用消息中的文字与图片，未内嵌时经 <c>get_message</c> 拉取）。
/// 单图上限 20 MB，单条消息最多 5 张图片。
/// </summary>
public sealed class GeminiInputReader : IDisposable
{
    /// <summary>单次对话最多附带的图片数。</summary>
    public const int MAX_IMAGES = 5;

    /// <summary>单张图片大小上限（20 MB，匹配 Gemini <c>inlineData</c> 限制）。</summary>
    public const long MAX_IMAGE_BYTES = 20L * 1024 * 1024;

    private const string DEFAULT_MEDIA_TYPE = "image/jpeg";
    private const int    BUFFER_BYTES       = 81920;

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
        foreach (ImageSegment segment in imageSegments)
        {
            (GeminiMediaPart? media, string? error) = await DownloadImageAsync(segment, cancellationToken);
            if (media is null)
            {
                return new GeminiInputReadResult
                {
                    Status       = GeminiInputReadStatus.Failed,
                    ErrorMessage = error ?? ReplyTexts.IMAGE_DOWNLOAD_FAILED
                };
            }

            downloaded.Add(media);
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

    private async Task<(GeminiMediaPart? Media, string? Error)> DownloadImageAsync(ImageSegment image, CancellationToken cancellationToken)
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

            if (response.Content.Headers.ContentLength > MAX_IMAGE_BYTES)
                return (null, ReplyTexts.IMAGE_TOO_LARGE);

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[BUFFER_BYTES];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > MAX_IMAGE_BYTES)
                    return (null, ReplyTexts.IMAGE_TOO_LARGE);

                buffer.Write(chunk, 0, read);
            }

            if (buffer.Length == 0) return (null, ReplyTexts.IMAGE_DOWNLOAD_FAILED);

            string mediaType = response.Content.Headers.ContentType?.MediaType is { } type
                               && type.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                ? type
                : DEFAULT_MEDIA_TYPE;

            return (new GeminiMediaPart
            {
                MediaType = mediaType,
                Data      = buffer.ToArray()
            }, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("下载 Gemini 输入图片异常：{Message}", exception.Message);
            return (null, ReplyTexts.IMAGE_DOWNLOAD_FAILED);
        }
    }
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
