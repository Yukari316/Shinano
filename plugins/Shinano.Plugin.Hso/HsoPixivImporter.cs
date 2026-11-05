using System.Globalization;
using Microsoft.Extensions.Logging;
using Shinano.Plugin.Hso.Models;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Models;

namespace Shinano.Plugin.Hso;

/// <summary>
/// <c>AD&lt;pid&gt; &lt;页号&gt;</c>：经图源服务查询 Pixiv 作品并下载指定页导入图库。为判断作品与页号是否存在必须先查作品信息，
/// 查到的信息随导入一并保存，发送时不必再查。
/// </summary>
public sealed class HsoPixivImporter
{
    private const long   MAX_DOWNLOAD_BYTES = 200L * 1024 * 1024;
    private const int    BUFFER_SIZE        = 81920;
    private const string PIXIV              = "Pixiv";

    private readonly YukariGateway             _gateway;
    private readonly ImageResolver             _resolver;
    private readonly HsoGallery                _gallery;
    private readonly ILogger<HsoPixivImporter> _logger;

    /// <summary>创建导入器。</summary>
    public HsoPixivImporter(YukariGateway gateway, ImageResolver resolver, HsoGallery gallery, ILogger<HsoPixivImporter> logger)
    {
        _gateway  = gateway;
        _resolver = resolver;
        _gallery  = gallery;
        _logger   = logger;
    }

    /// <summary>导入 Pixiv 作品的一页。</summary>
    public async Task<HsoPixivImportOutcome> ImportAsync(long pid, int page, CancellationToken cancellationToken)
    {
        if (await _gallery.HasPixivPageAsync(pid, page)) return HsoPixivImportOutcome.Exists;
        if (!_gateway.IsConfigured)
        {
            _logger.LogWarning("未配置 Hso 图源服务令牌，无法导入 Pixiv 作品 {Pid}", pid);
            return HsoPixivImportOutcome.Failed;
        }

        SourceRef source = new()
        {
            Site = ImageSite.Pixiv,
            Id   = pid.ToString(CultureInfo.InvariantCulture),
            Page = page
        };
        ImageResolution work = await _resolver.ResolveAsync([source], cancellationToken);
        if (work.Deleted || page >= work.PageCount) return HsoPixivImportOutcome.NotFound;
        if (work.PageCount is null) return HsoPixivImportOutcome.Failed;

        if (await DownloadAsync(source, cancellationToken) is not { } content) return HsoPixivImportOutcome.Failed;

        HsoImportResult result = await _gallery.ImportAsync(content, null, HsoImageOrigin.Pixiv, pid, page, cancellationToken);
        if (result.Outcome == HsoImportOutcome.Duplicate) return HsoPixivImportOutcome.Exists;
        if (result is not { Outcome: HsoImportOutcome.Imported, Id: { } id } || await _gallery.FindAsync(id) is not { } record)
            return HsoPixivImportOutcome.Failed;

        await _gallery.ApplyLookupAsync(
            record,
            new HsoLookup
            {
                Status = HsoLookupStatus.Found,
                Info = new HsoWorkInfo
                {
                    SourceName = PIXIV,
                    Pid        = pid,
                    Page       = page,
                    Title      = work.Title,
                    Author     = work.Author,
                    AuthorId   = work.AuthorId,
                    Tags       = HsoInfoResolver.DisplayTags(work),
                    Nsfw       = work.Adult
                }
            });
        return HsoPixivImportOutcome.Imported;
    }

    private async Task<byte[]?> DownloadAsync(SourceRef source, CancellationToken cancellationToken)
    {
        using HttpResponseMessage? response = await _gateway.OpenOriginalAsync(source, cancellationToken);
        if (response is null) return null;
        if (response.Content.Headers.ContentLength > MAX_DOWNLOAD_BYTES)
        {
            _logger.LogWarning("Pixiv 作品 {Pid} 第 {Page} 页 {Size}，超过下载上限", source.Id, source.Page, ImageInspector.FormatSize(response.Content.Headers.ContentLength));
            return null;
        }

        // 下载期间每秒记录进度与速度，读完时记录总量。
        await using Stream stream = new DownloadProgressStream(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            response.Content.Headers.ContentLength,
            $" Pixiv 作品 {source.Id} 第 {source.Page} 页",
            _logger);
        using MemoryStream buffer = new();
        byte[] chunk = new byte[BUFFER_SIZE];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length <= MAX_DOWNLOAD_BYTES) continue;
            _logger.LogWarning("Pixiv 作品 {Pid} 第 {Page} 页超过下载上限 {Limit}", source.Id, source.Page, ImageInspector.FormatSize(MAX_DOWNLOAD_BYTES));
            return null;
        }

        return buffer.ToArray();
    }
}
