using System.Globalization;
using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.ImageSearch.Models;

namespace Shinano.Plugin.ImageSearch.Delivery;

/// <summary>
/// 搜索结果卡片（宿主信息卡）：标签为搜索服务，相似度以刻度显示并标出阈值，低于阈值时为警告色。
/// 不放链接，避免消息中链接过多触发风控；R-18 不在卡片中标注，而是以替代图代替原图。
/// </summary>
public static class SearchCards
{
    private const string SIMILARITY_FORMAT = "0.##";
    private const string META_ILLUST       = "ILLUST";
    private const string META_ANIME        = "ANIME";
    private const string LOW_NOTE          = "相似度低于阈值，可能不准";

    /// <summary>插画结果卡片：标题为来源名，正文为标题、作者、作品号与页数（多于 1 页时）。</summary>
    public static InfoCard Illustration(SearchHit hit, ImageResolution resolution, double threshold, bool lowSimilarity) =>
        Card(
            hit,
            META_ILLUST,
            hit.SourceName,
            threshold,
            lowSimilarity,
            Row("标题", hit.Title ?? resolution.Title),
            Row("作者", hit.Author ?? resolution.Author),
            Row("作品号", hit.WorkId),
            Row("页数", resolution.PageCount > 1 ? $"共 {resolution.PageCount} 张" : null));

    /// <summary>番剧卡片：标题为番名，正文为集数、时间点与来源库。</summary>
    public static InfoCard Anime(SearchHit hit, double threshold, bool lowSimilarity) =>
        Card(
            hit,
            META_ANIME,
            hit.Title ?? hit.SourceName,
            threshold,
            lowSimilarity,
            Row("集数", hit.Episode),
            Row("时间点", hit.Time),
            Row("来源", hit.SourceName));

    /// <summary>卡片无法绘制时代替发送的文字。</summary>
    public static string ToText(InfoCard card) =>
        string.Join(
            '\n',
            (string[])
            [
                card.Title,
                .. card.Rows.Select(row => $"{row.Label}：{row.Text}"),
                .. card.Similarity is { } similarity ? (string[]) [$"相似度：{Percent(similarity)}"] : [],
                .. card.Note is { } note ? (string[]) [note] : []
            ]);

    private static InfoCard Card(SearchHit hit, string meta, string title, double threshold, bool lowSimilarity, params CardRow?[] rows) =>
        new()
        {
            Label      = EngineLabel(hit.Engine),
            Meta       = meta,
            Title      = title,
            Level      = lowSimilarity ? CardLevel.Warning : CardLevel.Normal,
            Similarity = hit.Similarity,
            Threshold  = threshold,
            Rows       = [.. rows.OfType<CardRow>()],
            Note       = lowSimilarity ? LOW_NOTE : null
        };

    private static string EngineLabel(SearchEngine engine) =>
        engine switch
        {
            SearchEngine.SauceNao => "SAUCENAO",
            SearchEngine.Iqdb     => "IQDB",
            _                     => "TRACE.MOE"
        };

    private static CardRow? Row(string label, string? text) =>
        text is null
            ? null
            : new CardRow
            {
                Label = label,
                Text  = text
            };

    private static string Percent(double similarity) => similarity.ToString(SIMILARITY_FORMAT, CultureInfo.InvariantCulture) + "%";
}
