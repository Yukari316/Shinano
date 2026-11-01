using System.Text.Json.Nodes;
using Shinano.Plugin.ImageSearch.Models;
using Shinano.Plugin.ImageSearch.Parsing;

namespace Shinano.Plugin.ImageSearch.Delivery;

/// <summary>
/// 按来源决定原图：依 <see cref="ImageSite" /> 的声明顺序逐个查询作品信息，第一个查得到的来源决定是否发图。
/// 图站帖子标注的来源是 Pixiv 时先取 Pixiv，并按 Pixiv 的信息判定 R-18；Pixiv 查不到时才用图站自己的原图与分级。
/// R-18 判定：Pixiv 为 <c>x_restrict &gt; 0</c> 或带 R-18/R-18G 标签；booru 与 Sankaku 为分级 e/explicit；e-shuushuu 为全年龄站。
/// </summary>
public sealed class ImageResolver
{
    private static readonly string[] AdultTags     = ["R-18", "R-18G"];
    private static readonly string[] ExplicitRates = ["e", "explicit"];

    private readonly YukariGateway _gateway;

    /// <summary>创建解析器。</summary>
    public ImageResolver(YukariGateway gateway) => _gateway = gateway;

    /// <summary>为搜索结果的来源决定原图。</summary>
    public async Task<ImageResolution> ResolveAsync(IReadOnlyList<SourceRef> sources, CancellationToken cancellationToken)
    {
        HashSet<string> triedPixiv = [];
        bool deleted = false;
        foreach (SourceRef source in sources.Where(source => source.Site != ImageSite.Konachan).OrderBy(source => source.Site))
        {
            if (source.Site == ImageSite.Pixiv)
            {
                if (!triedPixiv.Add(source.Id)) continue;
                WorkLookup illust = await _gateway.GetWorkAsync(source, cancellationToken);
                if (FromPixiv(source, illust.Data) is { } resolved) return resolved;
                deleted |= illust.NotFound;
                continue;
            }

            WorkLookup lookup = await _gateway.GetWorkAsync(source, cancellationToken);
            if (FirstPost(lookup.Data) is not { } post) continue;

            if (PixivSourceOf(post) is { } pixiv && triedPixiv.Add(pixiv.Id))
            {
                WorkLookup illust = await _gateway.GetWorkAsync(pixiv, cancellationToken);
                if (FromPixiv(pixiv, illust.Data) is { } resolved) return resolved;
            }

            bool adult = source.Site != ImageSite.Eshuushuu && JsonFields.Text(post["rating"]) is { } rating && ExplicitRates.Contains(rating, StringComparer.OrdinalIgnoreCase);
            return new ImageResolution
            {
                Image  = adult ? null : source,
                Adult  = adult,
                Author = JsonFields.Text(post["tag_string_artist"])
            };
        }

        return new ImageResolution { Deleted = deleted };
    }

    private static ImageResolution? FromPixiv(SourceRef source, JsonNode? data)
    {
        if (data?["illust"] is not JsonObject illust) return null;

        int pageCount = (int)(JsonFields.Number(illust["page_count"]) ?? 1);
        bool adult = JsonFields.Number(illust["x_restrict"]) > 0
                  || (illust["tags"] as JsonArray ?? []).Any(tag => JsonFields.Text(tag?["name"]) is { } name && AdultTags.Contains(name));
        int page = source.Page is { } requested && requested < pageCount ? requested : 0;
        return new ImageResolution
        {
            Image = adult
                ? null
                : new SourceRef
                {
                    Site = ImageSite.Pixiv,
                    Id   = source.Id,
                    Page = page
                },
            Adult     = adult,
            Title     = JsonFields.Text(illust["title"]),
            Author    = JsonFields.Text(illust["user"]?["name"]),
            PageCount = pageCount
        };
    }

    // 各图站的帖子形状不同：Danbooru 为对象，Moebooru 为数组，Gelbooru 为 { post: [...] }。
    private static JsonObject? FirstPost(JsonNode? data) =>
        data switch
        {
            JsonArray posts                                => posts.FirstOrDefault() as JsonObject,
            JsonObject wrapper when wrapper["post"] is { } => FirstPost(wrapper["post"]),
            JsonObject post                                => post,
            _                                              => null
        };

    private static SourceRef? PixivSourceOf(JsonObject post) =>
        JsonFields.Text(post["pixiv_id"]) is { } pixivId
            ? new SourceRef
            {
                Site = ImageSite.Pixiv,
                Id   = pixivId
            }
            : SourceLinks.Parse(JsonFields.Text(post["source"])) is { Site: ImageSite.Pixiv } source ? source : null;
}
