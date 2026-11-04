using System.Text.RegularExpressions;
using Shinano.Plugin.ImageSources.Models;

namespace Shinano.Plugin.ImageSources.Parsing;

/// <summary>从搜索结果与作品信息中的链接识别图站与作品号。</summary>
public static partial class SourceLinks
{
    /// <summary>识别链接指向的作品；不是已知图站的作品页时返回 null。pximg 原图链接带页号。</summary>
    public static SourceRef? Parse(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (PximgLink().Match(url) is { Success: true } pximg)
            return new SourceRef
            {
                Site = ImageSite.Pixiv,
                Id   = pximg.Groups["id"].Value,
                Page = int.Parse(pximg.Groups["page"].Value)
            };

        foreach ((ImageSite site, Regex pattern) in Patterns)
        {
            if (pattern.Match(url) is { Success: true } match)
                return new SourceRef
                {
                    Site = site,
                    Id   = match.Groups["id"].Value
                };
        }

        return null;
    }

    private static readonly (ImageSite Site, Regex Pattern)[] Patterns =
    [
        (ImageSite.Pixiv, PixivLink()),
        (ImageSite.Danbooru, DanbooruLink()),
        (ImageSite.Yandere, YandereLink()),
        (ImageSite.Konachan, KonachanLink()),
        (ImageSite.Gelbooru, GelbooruLink()),
        (ImageSite.Sankaku, SankakuLink()),
        (ImageSite.Eshuushuu, EshuushuuLink())
    ];

    [GeneratedRegex(@"pximg\.net/.*/(?<id>\d+)_p(?<page>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PximgLink();

    [GeneratedRegex(@"pixiv\.net/(?:(?:[a-z]{2}/)?artworks/|member_illust\.php\?\S*?illust_id=)(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex PixivLink();

    [GeneratedRegex(@"danbooru\.donmai\.us/(?:posts|post/show)/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex DanbooruLink();

    [GeneratedRegex(@"yande\.re/post/show/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex YandereLink();

    [GeneratedRegex(@"konachan\.(?:com|net)/post/show/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex KonachanLink();

    [GeneratedRegex(@"gelbooru\.com/index\.php\?\S*?[?&]id=(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex GelbooruLink();

    [GeneratedRegex(@"sankakucomplex\.com/(?:[a-z]{2}/)?(?:post/show|posts)/(?<id>\w+)", RegexOptions.IgnoreCase)]
    private static partial Regex SankakuLink();

    [GeneratedRegex(@"e-shuushuu\.net/image/(?<id>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex EshuushuuLink();
}
