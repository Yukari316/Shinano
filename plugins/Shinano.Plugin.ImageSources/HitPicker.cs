namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 从一次搜索的结果中挑出采用的一条，Pixiv 优先：达到阈值的结果中有指向 Pixiv 作品的（结果本身是 Pixiv，或其来源链接是 Pixiv 作品）时，
/// 取其中相似度最高的一条；否则取相似度最高的结果。同一张图常同时出现在 Pixiv 与图站中、相似度几乎相同，而图站帖子未必标注 Pixiv 来源。
/// 搜图与 Hso 补全使用同一规则。
/// </summary>
public static class HitPicker
{
    /// <summary>挑出采用的结果；没有结果时为 null。</summary>
    /// <param name="hits">按相似度从高到低排列的结果。</param>
    /// <param name="threshold">相似度阈值，单位百分比；低于阈值的 Pixiv 结果不优先。</param>
    public static SearchHit? Pick(IReadOnlyList<SearchHit> hits, double threshold) =>
        hits.FirstOrDefault(hit => hit.Similarity >= threshold && hit.Sources.Any(source => source.Site == ImageSite.Pixiv)) ?? hits.FirstOrDefault();
}
