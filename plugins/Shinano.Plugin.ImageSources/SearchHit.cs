namespace Shinano.Plugin.ImageSources;

/// <summary>一条搜索结果，已从各搜索服务的格式整理为卡片与取原图所需的内容。</summary>
public sealed class SearchHit
{
    /// <summary>来自哪个搜索服务。</summary>
    public required SearchEngine Engine { get; init; }

    /// <summary>相似度，单位百分比。</summary>
    public required double Similarity { get; init; }

    /// <summary>来源名，作为卡片标题，如 Pixiv、Danbooru 或番名所在的库名。</summary>
    public required string SourceName { get; init; }

    /// <summary>类别。</summary>
    public required HitKind Kind { get; init; }

    /// <summary>结果所在的库：SauceNAO 为 <c>index_id</c>；其他搜索服务为 null。</summary>
    public int? DatabaseId { get; init; }

    /// <summary>是否已知为成人内容（SauceNAO 的 H-Anime 库、trace.moe 的成人番、IQDB 的 Explicit 标注）；为 false 时仍可能由作品信息判定为 R-18。</summary>
    public bool Adult { get; init; }

    /// <summary>作品标题或番名；没有时为 null。</summary>
    public string? Title { get; init; }

    /// <summary>作者；没有时为 null。</summary>
    public string? Author { get; init; }

    /// <summary>作品号；没有时为 null。</summary>
    public string? WorkId { get; init; }

    /// <summary>可取原图的来源，顺序不代表优先级。</summary>
    public IReadOnlyList<SourceRef> Sources { get; init; } = [];

    /// <summary>番剧集数；没有时为 null。</summary>
    public string? Episode { get; init; }

    /// <summary>番剧中的时间点；没有时为 null。</summary>
    public string? Time { get; init; }

    /// <summary>番剧画面预览图地址（trace.moe）；没有时为 null。</summary>
    public string? PreviewUrl { get; init; }
}

/// <summary>搜索结果的类别，决定卡片内容与是否取原图。</summary>
public enum HitKind
{
    /// <summary>插画等图片作品，按来源取原图。</summary>
    Illustration,

    /// <summary>番剧画面，只发番剧卡片（trace.moe 结果附预览图）。</summary>
    Anime
}

/// <summary>经图源服务转发的搜索服务。</summary>
public enum SearchEngine
{
    /// <summary>SauceNAO，以图搜图的首选，也收录番剧。</summary>
    SauceNao,

    /// <summary>IQDB，SauceNAO 没有可信结果时的兜底，只覆盖 booru 类图站。</summary>
    Iqdb,

    /// <summary>trace.moe，以动画截图搜番。</summary>
    TraceMoe
}
