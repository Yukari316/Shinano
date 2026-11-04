using System.Globalization;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Entities;
using Shinano.Plugin.Hso.Models;
using Shinano.Plugin.ImageSources;
using Shinano.Plugin.ImageSources.Models;
using Shinano.Plugin.ImageSources.Parsing;

namespace Shinano.Plugin.Hso;

/// <summary>
/// 被抽中的图片缺少作品信息时补全：有 pid 时查 Pixiv，没有 pid 时用 SauceNAO 以图搜图，取相似度最高且达到阈值的插画结果，
/// 再按其来源查作品信息（Pixiv 优先，与搜图的原图规则相同）。NSFW 判定同搜图：Pixiv 为 <c>x_restrict &gt; 0</c> 或 R-18/R-18G 标签，
/// 图站为分级 e/explicit，H 类来源一律为 NSFW。
/// </summary>
public sealed class HsoInfoResolver
{
    private const string PIXIV = "Pixiv";

    private readonly IPluginInitializationContext _context;
    private readonly YukariGateway                _gateway;
    private readonly ImageResolver                _resolver;
    private readonly ILogger<HsoInfoResolver>     _logger;

    /// <summary>创建补全器。</summary>
    public HsoInfoResolver(IPluginInitializationContext context, YukariGateway gateway, ImageResolver resolver, ILogger<HsoInfoResolver> logger)
    {
        _context  = context;
        _gateway  = gateway;
        _resolver = resolver;
        _logger   = logger;
    }

    /// <summary>查询作品信息；图源服务未配置或出错时为 <see cref="HsoLookupStatus.Failed" />，下次抽中再查。</summary>
    /// <param name="record">图片记录。</param>
    /// <param name="content">图片内容，没有 pid 时上传给 SauceNAO；对象读取失败时为 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<HsoLookup> LookupAsync(HsoImageRecord record, byte[]? content, CancellationToken cancellationToken)
    {
        if (!_gateway.IsConfigured)
        {
            _logger.LogWarning("未配置 Hso 图源服务令牌，跳过作品信息补全");
            return Failed();
        }

        return record.Pid is { } pid
            ? await FromPixivAsync(pid, record.Page, cancellationToken)
            : content is null ? Failed() : await FromSauceNaoAsync(record, content, cancellationToken);
    }

    private async Task<HsoLookup> FromPixivAsync(long pid, int? page, CancellationToken cancellationToken)
    {
        SourceRef source = new()
        {
            Site = ImageSite.Pixiv,
            Id   = pid.ToString(CultureInfo.InvariantCulture),
            Page = page
        };
        ImageResolution resolution = await _resolver.ResolveAsync([source], cancellationToken);
        if (resolution.Deleted) return NotFound();
        if (resolution.PageCount is null) return Failed();

        return Found(new HsoWorkInfo
        {
            SourceName = PIXIV,
            Pid        = pid,
            Page       = page,
            Title      = resolution.Title,
            Author     = resolution.Author,
            AuthorId   = resolution.AuthorId,
            Tags       = resolution.Tags,
            Nsfw       = resolution.Adult
        });
    }

    private async Task<HsoLookup> FromSauceNaoAsync(HsoImageRecord record, byte[] content, CancellationToken cancellationToken)
    {
        SearchResult result = SauceNaoParser.Parse(await _gateway.SearchAsync(SearchEngine.SauceNao, content, record.MediaType, cancellationToken));
        if (result.Status != SearchStatus.Ok)
        {
            _logger.LogWarning("Hso 图片 {Id} 用 SauceNAO 补全作品信息失败：{Status} {Detail}", record.Id, result.Status, result.Detail);
            return Failed();
        }

        int threshold = _context.Config<HsoConfig>().Value.SauceNaoThreshold;
        if (result.Hits.FirstOrDefault() is not { Kind: HitKind.Illustration } hit || hit.Similarity < threshold)
        {
            _logger.LogInformation("Hso 图片 {Id} 在 SauceNAO 没有达到 {Threshold}% 的插画结果", record.Id, threshold);
            return NotFound();
        }

        ImageResolution resolution = hit.Adult ? ImageResolution.None : await _resolver.ResolveAsync(hit.Sources, cancellationToken);
        SourceRef? pixiv = hit.Sources.FirstOrDefault(source => source.Site == ImageSite.Pixiv);
        long? pid = pixiv is not null && long.TryParse(pixiv.Id, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : null;
        return Found(new HsoWorkInfo
        {
            SourceName = pid is null ? hit.SourceName : PIXIV,
            Pid        = pid,
            Page       = pid is null ? null : pixiv?.Page,
            Title      = hit.Title ?? resolution.Title,
            Author     = hit.Author ?? resolution.Author,
            AuthorId   = resolution.AuthorId,
            Tags       = resolution.Tags,
            Nsfw       = hit.Adult || resolution.Adult
        });
    }

    private static HsoLookup Found(HsoWorkInfo info) =>
        new()
        {
            Status = HsoLookupStatus.Found,
            Info   = info
        };

    private static HsoLookup NotFound() => new() { Status = HsoLookupStatus.NotFound };

    private static HsoLookup Failed() => new() { Status = HsoLookupStatus.Failed };
}
