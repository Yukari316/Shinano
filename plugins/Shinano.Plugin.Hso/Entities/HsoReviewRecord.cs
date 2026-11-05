using SqlSugar;

namespace Shinano.Plugin.Hso.Entities;

/// <summary>
/// 一张待审核图片的审核记录，位于全局 schema：补全查到了作品信息，但来源原图与图库图片的 SHA256 不一致或原图下载失败。
/// 查到的作品信息已写入 <see cref="HsoImageRecord" />（状态为待审核）；这里记录查询方式与暂存在插件 bucket 中的来源原图，
/// 处理后连同暂存对象一并删除。
/// </summary>
[SugarTable("hso_review")]
public sealed class HsoReviewRecord
{
    /// <summary>图片 ID（<see cref="HsoImageRecord.Id" />）。</summary>
    [SugarColumn(ColumnName = "image_id", IsPrimaryKey = true)]
    public long ImageId { get; set; }

    /// <summary>查询方式（<see cref="Models.HsoLookupMethod" /> 的数值）。</summary>
    [SugarColumn(ColumnName = "method")]
    public int Method { get; set; }

    /// <summary>SauceNAO 结果的相似度，单位百分比；按 pid 查询时为 null。</summary>
    [SugarColumn(ColumnName = "similarity", IsNullable = true)]
    public double? Similarity { get; set; }

    /// <summary>来源作品的图站（<see cref="ImageSources.Models.ImageSite" /> 的数值）；没有可下载的来源时为 null。</summary>
    [SugarColumn(ColumnName = "work_site", IsNullable = true)]
    public int? WorkSite { get; set; }

    /// <summary>来源作品号。</summary>
    [SugarColumn(ColumnName = "work_id", Length = 64, IsNullable = true)]
    public string? WorkId { get; set; }

    /// <summary>来源作品的页号（Pixiv）。</summary>
    [SugarColumn(ColumnName = "work_page", IsNullable = true)]
    public int? WorkPage { get; set; }

    /// <summary>暂存的来源原图在插件 bucket 中的 key；原图下载失败时为 null。</summary>
    [SugarColumn(ColumnName = "object_key", Length = 120, IsNullable = true)]
    public string? ObjectKey { get; set; }

    /// <summary>来源原图的 SHA256（小写十六进制）。</summary>
    [SugarColumn(ColumnName = "sha256", Length = 64, IsNullable = true)]
    public string? Sha256 { get; set; }

    /// <summary>来源原图的 MIME 类型。</summary>
    [SugarColumn(ColumnName = "media_type", Length = 50, IsNullable = true)]
    public string? MediaType { get; set; }

    /// <summary>来源原图的大小，单位字节。</summary>
    [SugarColumn(ColumnName = "size", IsNullable = true)]
    public long? Size { get; set; }

    /// <summary>来源原图的宽度，单位像素。</summary>
    [SugarColumn(ColumnName = "width", IsNullable = true)]
    public int? Width { get; set; }

    /// <summary>来源原图的高度，单位像素。</summary>
    [SugarColumn(ColumnName = "height", IsNullable = true)]
    public int? Height { get; set; }

    /// <summary>原图下载失败的原因（给管理员看）；下载成功时为 null。</summary>
    [SugarColumn(ColumnName = "fetch_error", Length = 200, IsNullable = true)]
    public string? FetchError { get; set; }

    /// <summary>进入审核队列的时间（UTC）。</summary>
    [SugarColumn(ColumnName = "created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
