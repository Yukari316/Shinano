using System.Globalization;
using Microsoft.Extensions.Logging;
using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Gateway;

namespace Shinano.Plugin.Hso.Gallery;

/// <summary>
/// <c>AD&lt;pid&gt; &lt;页号&gt;</c>：经图源服务查询 Pixiv 作品并下载指定页导入图库。为判断作品与页号是否存在必须先查作品信息，
/// 查到的信息随导入一并保存并直接确认（图片就是从 Pixiv 下载的原图，不进入审核），发送时不必再查。
/// </summary>
public sealed class HsoPixivImporter
{
    private const string PIXIV = "Pixiv";

    private readonly YukariGateway             _gateway;
    private readonly ImageResolver             _resolver;
    private readonly HsoOriginals              _originals;
    private readonly HsoGallery                _gallery;
    private readonly ILogger<HsoPixivImporter> _logger;

    /// <summary>创建导入器。</summary>
    public HsoPixivImporter(YukariGateway gateway, ImageResolver resolver, HsoOriginals originals, HsoGallery gallery, ILogger<HsoPixivImporter> logger)
    {
        _gateway   = gateway;
        _resolver  = resolver;
        _originals = originals;
        _gallery   = gallery;
        _logger    = logger;
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

        if ((await _originals.DownloadAsync(source, cancellationToken)).Content is not { } content) return HsoPixivImportOutcome.Failed;

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
                    Method     = HsoLookupMethod.Pixiv,
                    Work       = work.Work,
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
}

/// <summary>Pixiv 作品的一页。</summary>
public sealed class HsoPixivPage
{
    /// <summary>作品号。</summary>
    public required long Pid { get; init; }

    /// <summary>页号，从 0 开始。</summary>
    public required int Page { get; init; }
}

/// <summary><c>AD</c> 导入的结果。</summary>
public enum HsoPixivImportOutcome
{
    /// <summary>已导入。</summary>
    Imported,

    /// <summary>该作品页或相同内容的图片已在图库中。</summary>
    Exists,

    /// <summary>作品不存在、已删除或页号越界。</summary>
    NotFound,

    /// <summary>图源服务未配置、查询或下载失败，或写入失败。</summary>
    Failed
}
