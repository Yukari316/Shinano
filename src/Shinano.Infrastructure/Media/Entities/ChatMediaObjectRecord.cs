using SqlSugar;

namespace Shinano.Infrastructure.Media.Entities;

/// <summary>
/// 聊天媒体对象，存于 <c>&lt;prefix&gt;-chat</c>，按内容 SHA-256 在部署内复用；
/// 超过聊天记录保留期未再被引用时清理。
/// </summary>
[SugarTable("chat_media_object")]
[SugarIndex("ux_chat_media_object_sha256", nameof(Sha256), OrderByType.Asc, IsUnique = true)]
[SugarIndex("ix_chat_media_object_last_referenced", nameof(LastReferencedAt), OrderByType.Asc)]
public sealed class ChatMediaObjectRecord
{
    /// <summary>聊天媒体 ID，聊天记录中以该整数引用媒体。</summary>
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

    /// <summary>对象 key，每次上传唯一。</summary>
    [SugarColumn(ColumnName = "object_key", Length = 160)]
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>最近一次被新消息引用的时间。</summary>
    [SugarColumn(ColumnName = "last_referenced_at")]
    public DateTimeOffset LastReferencedAt { get; set; }

    /// <summary>创建时间。</summary>
    [SugarColumn(ColumnName = "created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
