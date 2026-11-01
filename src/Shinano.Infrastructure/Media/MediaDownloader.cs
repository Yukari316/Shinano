using Shinano.Infrastructure.Media.Models;

namespace Shinano.Infrastructure.Media;

/// <summary>按大小上限下载媒体：声明大小超限时不发起下载，下载中按实际字节计数，超限立即中止。</summary>
public sealed class MediaDownloader
{
    private const long BYTES_PER_MIB = 1024L * 1024;

    private readonly HttpClient _httpClient;

    /// <summary>创建下载器。</summary>
    /// <param name="httpClient">调用方管理生命周期的 HTTP 客户端。</param>
    /// <param name="options">媒体配置，提供默认上限。</param>
    public MediaDownloader(HttpClient httpClient, MediaOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxFileSizeMiB);
        _httpClient       = httpClient;
        DefaultLimitBytes = options.MaxFileSizeMiB * BYTES_PER_MIB;
    }

    /// <summary>部署配置的单文件上限，单位字节。</summary>
    public long DefaultLimitBytes { get; }

    /// <summary>下载 <paramref name="url" /> 到临时文件。</summary>
    /// <param name="url">媒体地址。</param>
    /// <param name="declaredSize">消息段声明的大小，未知时为 null。</param>
    /// <param name="limitBytes">本次上限，为 null 时使用 <see cref="DefaultLimitBytes" />；不得高于默认上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="MediaTooLargeException">声明大小、响应长度或实际内容超过上限。</exception>
    /// <exception cref="HttpRequestException">请求失败或返回非成功状态码。</exception>
    public async Task<BufferedMedia> DownloadAsync(
        Uri               url,
        long?             declaredSize,
        long?             limitBytes,
        CancellationToken cancellationToken)
    {
        long limit = Math.Min(limitBytes ?? DefaultLimitBytes, DefaultLimitBytes);
        if (declaredSize > limit) throw new MediaTooLargeException(limit);

        using HttpResponseMessage response =
            await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new MediaTooLargeException(limit);

        await using Stream content = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await BufferedMedia.CreateAsync(content, limit, cancellationToken);
    }
}
