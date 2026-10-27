using SqlSugar;

namespace Shinano.Infrastructure.Media.Entities;

/// <summary>持久媒体对象，存于 <c>&lt;prefix&gt;-media</c>，按内容 SHA-256 去重，生命周期由引用记录决定。</summary>
[SugarTable("media_object")]
[SugarIndex("ux_media_object_sha256", nameof(Sha256), OrderByType.Asc, IsUnique = true)]
public sealed class MediaObjectRecord
{
    /// <summary>媒体 ID。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>内容 SHA-256 的小写十六进制。</summary>
    [SugarColumn(ColumnName = "sha256", Length = 64)]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>字节数。</summary>
    [SugarColumn(ColumnName = "size")]
    public long Size { get; set; }

    /// <summary>MIME 类型。</summary>
    [SugarColumn(ColumnName = "media_type", Length = 128)]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>对象 key。每次上传唯一，避免并发回收误删同内容的新对象。</summary>
    [SugarColumn(ColumnName = "object_key", Length = 160)]
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>最后一条引用被删除的时间；有引用时为 null。</summary>
    [SugarColumn(ColumnName = "orphaned_at", IsNullable = true)]
    public DateTimeOffset? OrphanedAt { get; set; }

    /// <summary>创建时间。</summary>
    [SugarColumn(ColumnName = "created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
