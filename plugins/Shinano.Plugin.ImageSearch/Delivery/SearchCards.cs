using System.Globalization;
using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.ImageSearch.Models;

namespace Shinano.Plugin.ImageSearch.Delivery;

/// <summary>
/// 搜索结果卡片（宿主文本长图）。不放链接，避免消息中链接过多触发风控；相似度低于阈值时竖条为黄色。
/// R-18 不在卡片中标注，而是以替代图代替原图。
/// </summary>
public static class SearchCards
{
    private const string SIMILARITY_FORMAT = "0.##";

    /// <summary>插画结果卡片：标题为来源名，正文为标题、作者、作品号、页数（多于 1 页时）与相似度。</summary>
    public static TextImageDocument Illustration(SearchHit hit, ImageResolution resolution, bool lowSimilarity) =>
        Card(
            hit.SourceName,
            lowSimilarity,
            Row("标题", hit.Title ?? resolution.Title),
            Row("作者", hit.Author ?? resolution.Author),
            Row("作品号", hit.WorkId),
            Row("页数", resolution.PageCount > 1 ? $"共 {resolution.PageCount} 张" : null),
            Row("相似度", Percent(hit.Similarity)));

    /// <summary>番剧卡片：标题为番名，正文为集数、时间点与相似度。</summary>
    public static TextImageDocument Anime(SearchHit hit, bool lowSimilarity) =>
        Card(
            hit.Title ?? hit.SourceName,
            lowSimilarity,
            Row("集数", hit.Episode),
            Row("时间点", hit.Time),
            Row("相似度", Percent(hit.Similarity)),
            Row("来源", hit.SourceName));

    private static TextImageDocument Card(string title, bool lowSimilarity, params TextImageRow?[] rows) =>
        new()
        {
            Title = title,
            Level = lowSimilarity ? TextImageLevel.Warning : TextImageLevel.Normal,
            Rows  = [.. rows.OfType<TextImageRow>()]
        };

    private static TextImageRow? Row(string label, string? text) =>
        text is null
            ? null
            : new TextImageRow
            {
                Label = label,
                Text  = text
            };

    private static string Percent(double similarity) => similarity.ToString(SIMILARITY_FORMAT, CultureInfo.InvariantCulture) + "%";
}
