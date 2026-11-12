using Microsoft.Extensions.Logging;
using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Media;
using Shinano.Infrastructure.Media.Models;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;
using System.Net.Sockets;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>
/// 消息写入后的后台归档：展开只有转发 ID 的合并转发，下载媒体存为聊天媒体，并在段 JSON 中写入媒体 ID。
/// 任何一项失败都只标记内容缺失并告警，不影响已写入的正文与统计。
/// </summary>
public sealed class ChatMediaArchiver
{
    private const string BASE64_SCHEME      = "base64://";
    private const int    BASE64_BLOCK_CHARS = 4;
    private const int    BASE64_BLOCK_BYTES = 3;

    private readonly ChatMediaStore             _store;
    private readonly MediaDownloader            _downloader;
    private readonly DependencyAvailability     _storageAvailability;
    private readonly ILogger<ChatMediaArchiver> _logger;

    /// <summary>创建归档器。</summary>
    public ChatMediaArchiver(
        ChatMediaStore             store,
        MediaDownloader            downloader,
        DependencyAvailability     storageAvailability,
        ILogger<ChatMediaArchiver> logger)
    {
        _store               = store;
        _downloader          = downloader;
        _storageAvailability = storageAvailability;
        _logger              = logger;
    }

    /// <summary>完成一条消息的后台归档。</summary>
    /// <param name="entry">已写入的归档内容，段 JSON 在此被就地补全。</param>
    /// <param name="api">当前连接；断线时为 null，需要协议接口的项目标记缺失。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>归档完成后的状态，不再含 <see cref="ChatArchiveFlags.MediaPending" />。</returns>
    public async Task<ChatArchiveFlags> ArchiveAsync(
        ChatArchiveEntry  entry,
        IBotApi?          api,
        CancellationToken cancellationToken)
    {
        ChatSegmentEncoder encoder = entry.Encoder;
        // 展开时可能追加更深一层的转发与新的媒体，按下标遍历。
        for (int index = 0; index < encoder.PendingForwards.Count; index++)
            await ExpandForwardAsync(encoder, encoder.PendingForwards[index], api, entry, cancellationToken);

        foreach (PendingChatMedia media in encoder.PendingMedia)
        {
            long? mediaId = await ArchiveMediaAsync(media.Source, entry, api, cancellationToken);
            if (mediaId is { } id) media.Node["m"] = id;
            else encoder.MarkMissing();
        }

        return encoder.HasMissingContent ? ChatArchiveFlags.ContentMissing : ChatArchiveFlags.None;
    }

    private async Task ExpandForwardAsync(
        ChatSegmentEncoder encoder,
        PendingChatForward forward,
        IBotApi?           api,
        ChatArchiveEntry   entry,
        CancellationToken  cancellationToken)
    {
        if (api is null)
        {
            encoder.MarkMissing();
            return;
        }

        try
        {
            ApiResult<IReadOnlyList<MessageContext>> result =
                await api.GetForwardMessagesAsync(forward.ForwardId, cancellationToken);
            if (result is { IsSuccess: true, Data: { } messages })
            {
                forward.Node["msgs"] = encoder.EncodeForwardedMessages(messages, forward.Depth + 1);
                return;
            }

            _logger.LogWarning(
                "展开合并转发 {ForwardId} 失败（消息 {MessageId}）：{Code} {Error}",
                forward.ForwardId,
                entry.Message.MessageId,
                result.Code,
                result.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "展开合并转发 {ForwardId} 失败（消息 {MessageId}）", forward.ForwardId, entry.Message.MessageId);
        }

        encoder.MarkMissing();
    }

    private async Task<long?> ArchiveMediaAsync(
        ChatMediaSource   source,
        ChatArchiveEntry  entry,
        IBotApi?          api,
        CancellationToken cancellationToken)
    {
        if (!_storageAvailability.IsAvailable) return null;

        BufferedMedia? media;
        try
        {
            media = await FetchAsync(source, entry, api, cancellationToken);
        }
        catch (MediaTooLargeException exception)
        {
            _logger.LogWarning("聊天媒体超过大小上限 {Limit} 字节，未归档（消息 {MessageId}）", exception.LimitBytes, entry.Message.MessageId);
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "下载聊天媒体失败（消息 {MessageId}）", entry.Message.MessageId);
            return null;
        }

        if (media is null)
        {
            _logger.LogWarning("聊天媒体没有可用的获取方式（消息 {MessageId}，{Kind}）", entry.Message.MessageId, source.Kind);
            return null;
        }

        await using (media)
        {
            try
            {
                string mediaType = source.Kind == ChatMediaKind.File
                    ? MediaTypeSniffer.OCTET_STREAM
                    : await MediaTypeSniffer.DetectAsync(media.FilePath, cancellationToken);
                return await _store.StoreAsync(media, mediaType, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "保存聊天媒体失败（消息 {MessageId}）", entry.Message.MessageId);
                // 连接故障是 S3 客户端重试后仍失败，立即标记 RustFS 不可用，不等节流的探测；其他失败（如数据库写入）交给探测确认。
                if (IsConnectionFault(exception)) _storageAvailability.ReportFailure(exception);
                else _ = _storageAvailability.CheckAsync(force: false, CancellationToken.None);
                return null;
            }
        }
    }

    // 保存时只有 RustFS 走 HTTP：拒绝连接与 DNS 失败为 HttpRequestException，连接被重置为内含 SocketException 的 IOException。
    // Npgsql 的连接故障包装为 NpgsqlException，读取本地临时文件的 IOException 不含 SocketException，都不在此列。
    private static bool IsConnectionFault(Exception exception) =>
        exception is HttpRequestException or IOException { InnerException: SocketException };

    private async Task<BufferedMedia?> FetchAsync(
        ChatMediaSource   source,
        ChatArchiveEntry  entry,
        IBotApi?          api,
        CancellationToken cancellationToken)
    {
        if (IsHttp(source.Url)) return await DownloadAsync(source.Url, source, cancellationToken);
        if (IsHttp(source.FileUri)) return await DownloadAsync(source.FileUri, source, cancellationToken);
        if (source.FileUri.StartsWith(BASE64_SCHEME, StringComparison.Ordinal))
            return await DecodeAsync(source.FileUri[BASE64_SCHEME.Length..], cancellationToken);
        if (api is null) return null;

        string? url = await ResolveUrlAsync(source, entry, api, cancellationToken);
        return url is null ? null : await DownloadAsync(url, source, cancellationToken);
    }

    private static async Task<string?> ResolveUrlAsync(
        ChatMediaSource   source,
        ChatArchiveEntry  entry,
        IBotApi           api,
        CancellationToken cancellationToken)
    {
        ApiResult<string> result;
        if (source.ResourceId.Length > 0)
            result = await api.GetResourceTempUrlAsync(source.ResourceId, cancellationToken);
        else if (source.FileId.Length == 0)
            return null;
        else if (entry.Message.ConversationType == ChatConversationType.Group)
            result = await api.GetGroupFileDownloadUrlAsync(entry.Message.ConversationId, source.FileId, cancellationToken);
        else
            result = await api.GetPrivateFileDownloadUrlAsync(
                entry.Message.ConversationId,
                source.FileId,
                source.FileHash,
                entry.IsOutbound,
                cancellationToken);

        return result is { IsSuccess: true, Data: { Length: > 0 } url } ? url : null;
    }

    private async Task<BufferedMedia> DownloadAsync(string url, ChatMediaSource source, CancellationToken cancellationToken) =>
        await _downloader.DownloadAsync(new Uri(url), source.DeclaredSize, limitBytes: null, cancellationToken);

    private async Task<BufferedMedia> DecodeAsync(string base64, CancellationToken cancellationToken)
    {
        long limit = _downloader.DefaultLimitBytes;
        // 先按编码长度估算解码后的大小，避免解码超大内容。
        if ((long)base64.Length / BASE64_BLOCK_CHARS * BASE64_BLOCK_BYTES > limit) throw new MediaTooLargeException(limit);

        await using MemoryStream content = new(Convert.FromBase64String(base64), writable: false);
        return await BufferedMedia.CreateAsync(content, limit, cancellationToken);
    }

    private static bool IsHttp(string uri) =>
        uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
