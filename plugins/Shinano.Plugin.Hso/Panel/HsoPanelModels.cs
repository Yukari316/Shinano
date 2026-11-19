using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Persistence;

namespace Shinano.Plugin.Hso.Panel;

/// <summary>面板中的一张图片。</summary>
public sealed class HsoImageView
{
    /// <summary>图片 ID。</summary>
    public required long Id { get; init; }

    /// <summary>导入时的文件名。</summary>
    public string? FileName { get; init; }

    /// <summary>MIME 类型。</summary>
    public required string MediaType { get; init; }

    /// <summary>大小，单位字节。</summary>
    public required long Size { get; init; }

    /// <summary>Pixiv 作品号。</summary>
    public long? Pid { get; init; }

    /// <summary>Pixiv 页号。</summary>
    public int? Page { get; init; }

    /// <summary>标题。</summary>
    public string? Title { get; init; }

    /// <summary>作者。</summary>
    public string? Author { get; init; }

    /// <summary>作品信息的来源站名。</summary>
    public string? SourceName { get; init; }

    /// <summary>是否为 NSFW。</summary>
    public required bool Nsfw { get; init; }

    /// <summary>NSFW 是否由管理员手动设置。</summary>
    public required bool NsfwManual { get; init; }

    /// <summary>是否被拦截过。</summary>
    public required bool Blocked { get; init; }

    /// <summary>作品信息补全状态。</summary>
    public required HsoInfoState InfoState { get; init; }

    /// <summary>导入方式。</summary>
    public required HsoImageOrigin Origin { get; init; }

    /// <summary>导入时间（UTC）。</summary>
    public required DateTimeOffset ImportedAt { get; init; }

    /// <summary>由记录创建。</summary>
    public static HsoImageView From(HsoImageRecord record) =>
        new()
        {
            Id         = record.Id,
            FileName   = record.FileName,
            MediaType  = record.MediaType,
            Size       = record.Size,
            Pid        = record.Pid,
            Page       = record.Page,
            Title      = record.Title,
            Author     = record.Author,
            SourceName = record.SourceName,
            Nsfw       = record.Nsfw,
            NsfwManual = record.NsfwManual,
            Blocked    = record.Blocked,
            InfoState  = (HsoInfoState)record.InfoState,
            Origin     = (HsoImageOrigin)record.Origin,
            ImportedAt = record.ImportedAt
        };
}

/// <summary>面板图库的一页。</summary>
public sealed class HsoImagePage
{
    /// <summary>符合条件的总张数。</summary>
    public required int Total { get; init; }

    /// <summary>本页图片。</summary>
    public required IReadOnlyList<HsoImageView> Items { get; init; }
}

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

/// <summary>审核中对比的一张图片：图库中的图片或查询获取的原图。</summary>
public sealed class HsoReviewImage
{
    /// <summary>MIME 类型。</summary>
    public required string MediaType { get; init; }

    /// <summary>大小，单位字节。</summary>
    public required long Size { get; init; }

    /// <summary>宽度，单位像素；读取失败时为 null。</summary>
    public int? Width { get; init; }

    /// <summary>高度，单位像素；读取失败时为 null。</summary>
    public int? Height { get; init; }

    /// <summary>内容的 SHA256（小写十六进制）。</summary>
    public required string Sha256 { get; init; }
}

/// <summary>面板中一个群的设置。</summary>
public sealed class HsoGroupView
{
    /// <summary>群号。</summary>
    public required long GroupId { get; init; }

    /// <summary>群名；Bot 已不在该群或取不到时为 null。</summary>
    public string? Name { get; init; }

    /// <summary>是否屏蔽。</summary>
    public required bool Blocked { get; init; }

    /// <summary>是否允许 NSFW。</summary>
    public required bool AllowNsfw { get; init; }
}

/// <summary>面板修改群设置的请求。</summary>
public sealed class HsoGroupUpdate
{
    /// <summary>是否屏蔽。</summary>
    public bool Blocked { get; init; }

    /// <summary>是否允许 NSFW。</summary>
    public bool AllowNsfw { get; init; }
}

/// <summary>面板设置图片 NSFW 的请求。</summary>
public sealed class HsoNsfwUpdate
{
    /// <summary>是否为 NSFW。</summary>
    public bool Nsfw { get; init; }
}
