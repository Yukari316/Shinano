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
/// 查询图片的作品信息（由 <see cref="HsoCompleter" /> 在后台调用）：有 pid 时查 Pixiv，没有 pid 时用 SauceNAO 以图搜图，按 <see cref="HitPicker" />（Pixiv 优先）取达到阈值的插画结果，
/// 再按其来源查作品信息（Pixiv 优先，与搜图的规则相同）。NSFW 判定同搜图：Pixiv 为 <c>x_restrict &gt; 0</c> 或 R-18/R-18G 标签，
/// 图站为分级 e/explicit。SauceNAO 只检索插画图库（见 <see cref="SearchDatabases.SauceNaoIllustrations" />）。
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

    /// <summary>查询作品信息；图源服务未配置或出错时为 <see cref="HsoLookupStatus.Failed" />，之后再查。</summary>
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
            Method     = HsoLookupMethod.Pixiv,
            Work       = resolution.Work,
            SourceName = PIXIV,
            Pid        = pid,
            Page       = page,
            Title      = resolution.Title,
            Author     = resolution.Author,
            AuthorId   = resolution.AuthorId,
            Tags       = DisplayTags(resolution),
            Nsfw       = resolution.Adult
        });
    }

    private async Task<HsoLookup> FromSauceNaoAsync(HsoImageRecord record, byte[] content, CancellationToken cancellationToken)
    {
        IReadOnlySet<SauceNaoIndex> databases = SearchDatabases.SauceNaoIllustrations;
        SearchResult result = SauceNaoParser.Parse(await _gateway.SearchSauceNaoAsync(databases, content, record.MediaType, cancellationToken), databases);
        if (result.Status != SearchStatus.Ok)
        {
            _logger.LogWarning("Hso 图片 {Id} 用 SauceNAO 补全作品信息失败：{Status} {Detail}", record.Id, result.Status, result.Detail);
            return Failed();
        }

        int threshold = _context.Config<HsoConfig>().Value.SauceNaoThreshold;
        if (HitPicker.Pick(result.Hits, threshold) is not { Kind: HitKind.Illustration } hit || hit.Similarity < threshold)
        {
            _logger.LogInformation("Hso 图片 {Id} 在 SauceNAO 没有达到 {Threshold}% 的插画结果", record.Id, threshold);
            return NotFound();
        }

        ImageResolution resolution = hit.Adult ? ImageResolution.None : await _resolver.ResolveAsync(hit.Sources, cancellationToken);
        return Found(InfoFromSauceNao(hit, resolution));
    }

    /// <summary>
    /// SauceNAO 结果的作品信息。来源作品查到 Pixiv 时（结果本身是 Pixiv，或图站帖子标注的来源是 Pixiv），来源名、pid、页号、
    /// 标题、作者与标签都用 Pixiv 的作品信息：图站结果的「标题」常是作品出处、「作者」是图站的画师标签。查不到 Pixiv 时用搜索结果中的信息；
    /// 结果本身带 Pixiv 作品号（如作品已删除）时仍记录该 pid。
    /// </summary>
    public static HsoWorkInfo InfoFromSauceNao(SearchHit hit, ImageResolution resolution)
    {
        bool adult = hit.Adult || resolution.Adult;
        if (resolution.Work is { Site: ImageSite.Pixiv } work && long.TryParse(work.Id, NumberStyles.None, CultureInfo.InvariantCulture, out long pid))
            return new HsoWorkInfo
            {
                Method     = HsoLookupMethod.SauceNao,
                Similarity = hit.Similarity,
                Work       = work,
                SourceName = PIXIV,
                Pid        = pid,
                Page       = work.Page,
                Title      = resolution.Title,
                Author     = resolution.Author,
                AuthorId   = resolution.AuthorId,
                Tags       = DisplayTags(resolution),
                Nsfw       = adult
            };

        SourceRef? pixiv = hit.Sources.FirstOrDefault(source => source.Site == ImageSite.Pixiv);
        long? hitPid = pixiv is not null && long.TryParse(pixiv.Id, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : null;
        return new HsoWorkInfo
        {
            Method     = HsoLookupMethod.SauceNao,
            Similarity = hit.Similarity,
            Work       = resolution.Work,
            SourceName = hitPid is null ? hit.SourceName : PIXIV,
            Pid        = hitPid,
            Page       = hitPid is null ? null : pixiv?.Page,
            Title      = hit.Title ?? resolution.Title,
            Author     = hit.Author ?? resolution.Author,
            Nsfw       = adult
        };
    }

    /// <summary>信息卡显示的标签：只取 Pixiv 作品的标签，有中文翻译时用翻译，否则用原文，去掉重复。</summary>
    internal static IReadOnlyList<string> DisplayTags(ImageResolution resolution) =>
        resolution.Work?.Site == ImageSite.Pixiv ? [.. resolution.Tags.Select(tag => tag.Translation ?? tag.Name).Distinct()] : [];

    private static HsoLookup Found(HsoWorkInfo info) =>
        new()
        {
            Status = HsoLookupStatus.Found,
            Info   = info
        };

    private static HsoLookup NotFound() => new() { Status = HsoLookupStatus.NotFound };

    private static HsoLookup Failed() => new() { Status = HsoLookupStatus.Failed };
}
