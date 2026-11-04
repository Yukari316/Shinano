using System.Collections.Frozen;
using Shinano.Plugin.ImageSources.Models;

namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 搜索时限定的库。搜索服务只返回相似度最高的几条，不限定时容易被取不到原图的站（如 Nijie、Twitter、Zerochan）占满，
/// 支持的图库中相似度稍低的结果就被挤掉了，所以在请求时限定，解析时也只保留这些库的结果。
/// </summary>
public static class SearchDatabases
{
    /// <summary>SauceNAO 中能经图源服务取到原图的插画图库：搜图与 Hso 作品信息补全使用。</summary>
    public static IReadOnlySet<SauceNaoIndex> SauceNaoIllustrations { get; } = new[]
    {
        SauceNaoIndex.Pixiv,
        SauceNaoIndex.Danbooru,
        SauceNaoIndex.Yandere,
        SauceNaoIndex.Gelbooru,
        SauceNaoIndex.Sankaku
    }.ToFrozenSet();

    /// <summary>SauceNAO 的番剧库：搜番在 trace.moe 不可用时兜底使用。</summary>
    public static IReadOnlySet<SauceNaoIndex> SauceNaoAnime { get; } = new[] { SauceNaoIndex.Anime, SauceNaoIndex.HAnime }.ToFrozenSet();

    /// <summary>IQDB 中能经图源服务取到原图的图站：搜图在 SauceNAO 不可用时兜底使用。</summary>
    public static IReadOnlySet<ImageSite> IqdbIllustrations { get; } = new[]
    {
        ImageSite.Danbooru,
        ImageSite.Yandere,
        ImageSite.Gelbooru,
        ImageSite.Sankaku,
        ImageSite.Eshuushuu
    }.ToFrozenSet();
}
