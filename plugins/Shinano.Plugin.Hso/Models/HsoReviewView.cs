namespace Shinano.Plugin.Hso.Models;

/// <summary>审核队列中的一张图片（面板 <c>/reviews/{id}</c>）：两张图片的对比与查询到的作品信息。</summary>
public sealed class HsoReviewView
{
    /// <summary>图片 ID。</summary>
    public required long ImageId { get; init; }

    /// <summary>导入时的文件名。</summary>
    public string? FileName { get; init; }

    /// <summary>导入方式。</summary>
    public required HsoImageOrigin Origin { get; init; }

    /// <summary>导入时间（UTC）。</summary>
    public required DateTimeOffset ImportedAt { get; init; }

    /// <summary>图库中的图片。</summary>
    public required HsoReviewImage Current { get; init; }

    /// <summary>查询获取的原图；下载失败时为 null，原因见 <see cref="FetchError" />。</summary>
    public HsoReviewImage? Candidate { get; init; }

    /// <summary>原图下载失败的原因；下载成功时为 null。</summary>
    public string? FetchError { get; init; }

    /// <summary>查询方式。</summary>
    public required HsoLookupMethod Method { get; init; }

    /// <summary>SauceNAO 结果的相似度，单位百分比；按 pid 查询时为 null。</summary>
    public double? Similarity { get; init; }

    /// <summary>来源作品的图站名，如 Pixiv、Danbooru；没有可下载的来源时为 null。</summary>
    public string? WorkSite { get; init; }

    /// <summary>来源作品号。</summary>
    public string? WorkId { get; init; }

    /// <summary>来源作品的页号（Pixiv）。</summary>
    public int? WorkPage { get; init; }

    /// <summary>进入审核队列的时间（UTC）。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>查到的作品信息的来源站名。</summary>
    public string? SourceName { get; init; }

    /// <summary>标题。</summary>
    public string? Title { get; init; }

    /// <summary>作者。</summary>
    public string? Author { get; init; }

    /// <summary>Pixiv 作者 ID。</summary>
    public long? AuthorId { get; init; }

    /// <summary>Pixiv 作品号。</summary>
    public long? Pid { get; init; }

    /// <summary>Pixiv 页号。</summary>
    public int? Page { get; init; }

    /// <summary>标签。</summary>
    public required IReadOnlyList<string> Tags { get; init; }

    /// <summary>按查到的信息判定的 NSFW（管理员手动设置过时为其设置）。</summary>
    public required bool Nsfw { get; init; }
}
