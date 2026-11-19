using Microsoft.Extensions.Logging;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Gateway;

namespace Shinano.Plugin.Hso.Gallery;

/// <summary>经图源服务下载作品原图（Pixiv 的某一页或图站帖子）到内存，供 <c>AD</c> 导入与补全时比较 SHA256。</summary>
public sealed class HsoOriginals
{
    /// <summary>单张原图的下载上限，单位字节。</summary>
    public const long MAX_DOWNLOAD_BYTES = 200L * 1024 * 1024;

    private const int BUFFER_SIZE = 81920;

    private readonly YukariGateway         _gateway;
    private readonly ILogger<HsoOriginals> _logger;

    /// <summary>创建下载器。</summary>
    public HsoOriginals(YukariGateway gateway, ILogger<HsoOriginals> logger)
    {
        _gateway = gateway;
        _logger  = logger;
    }

    /// <summary>下载原图；下载期间每秒记录进度与速度。失败或超过 <see cref="MAX_DOWNLOAD_BYTES" /> 时给出原因，不抛出。</summary>
    public async Task<HsoDownload> DownloadAsync(SourceRef source, CancellationToken cancellationToken)
    {
        string label = source.Page is { } page ? $" {source.Site} 作品 {source.Id} 第 {page} 页" : $" {source.Site} 作品 {source.Id}";
        using HttpResponseMessage? response = await _gateway.OpenOriginalAsync(source, cancellationToken);
        if (response is null) return Failed("图源服务没有返回原图");

        long? length = response.Content.Headers.ContentLength;
        if (length > MAX_DOWNLOAD_BYTES)
        {
            _logger.LogWarning("下载{Label}：{Size}，超过下载上限", label, ImageInspector.FormatSize(length));
            return Failed($"原图 {ImageInspector.FormatSize(length)}，超过下载上限");
        }

        try
        {
            await using Stream stream = new DownloadProgressStream(await response.Content.ReadAsStreamAsync(cancellationToken), length, label, _logger);
            using MemoryStream buffer = new();
            byte[] chunk = new byte[BUFFER_SIZE];
            int read;
            while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length <= MAX_DOWNLOAD_BYTES) continue;
                _logger.LogWarning("下载{Label}超过下载上限 {Limit}", label, ImageInspector.FormatSize(MAX_DOWNLOAD_BYTES));
                return Failed("原图超过下载上限");
            }

            return new HsoDownload { Content = buffer.ToArray() };
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("下载{Label}中断：{Error}", label, exception.Message);
            return Failed("下载中断");
        }
    }

    private static HsoDownload Failed(string error) => new() { Error = error };
}

/// <summary>下载原图的结果：成功时有 <see cref="Content" />，失败时有 <see cref="Error" />。</summary>
public sealed class HsoDownload
{
    /// <summary>原图内容；失败时为 null。</summary>
    public byte[]? Content { get; init; }

    /// <summary>失败原因（给管理员看）；成功时为 null。</summary>
    public string? Error { get; init; }
}
