namespace Shinano.Plugin.ImageSearch.Models;

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

    /// <summary>是否已知为成人内容（H 类来源、成人番、IQDB 的 Explicit 标注）；为 false 时仍可能由作品信息判定为 R-18。</summary>
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
