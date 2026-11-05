using Shinano.Plugin.ImageSources.Models;

namespace Shinano.Plugin.Hso.Models;

/// <summary>补全得到的作品信息。</summary>
public sealed class HsoWorkInfo
{
    /// <summary>查询方式。</summary>
    public required HsoLookupMethod Method { get; init; }

    /// <summary>SauceNAO 结果的相似度，单位百分比；按 pid 查询时为 null。</summary>
    public double? Similarity { get; init; }

    /// <summary>查到作品信息的来源（Pixiv 作品的某一页或图站帖子），用于下载原图比较；没有时为 null。</summary>
    public SourceRef? Work { get; init; }

    /// <summary>来源站名，如 Pixiv。</summary>
    public required string SourceName { get; init; }

    /// <summary>Pixiv 作品号；来源不是 Pixiv 时为 null。</summary>
    public long? Pid { get; init; }

    /// <summary>Pixiv 页号；未知时为 null。</summary>
    public int? Page { get; init; }

    /// <summary>标题。</summary>
    public string? Title { get; init; }

    /// <summary>作者。</summary>
    public string? Author { get; init; }

    /// <summary>Pixiv 作者的用户 ID；来源不是 Pixiv 时为 null。</summary>
    public long? AuthorId { get; init; }

    /// <summary>Pixiv 标签（有中文翻译时为翻译，否则为原文）；来源不是 Pixiv 时为空。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>是否为 NSFW。</summary>
    public required bool Nsfw { get; init; }
}
