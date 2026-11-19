using SqlSugar;

namespace Shinano.Infrastructure.Media;

/// <summary>持久媒体的引用记录：哪个插件的哪个业务对象（在哪个账号下）使用了某个媒体。</summary>
[SugarTable("media_ref")]
[SugarIndex("ix_media_ref_media", nameof(MediaId), OrderByType.Asc)]
public sealed class MediaReferenceRecord
{
    /// <summary>媒体 ID。</summary>
    [SugarColumn(ColumnName = "media_id", IsPrimaryKey = true)]
    public long MediaId { get; set; }

    /// <summary>持有引用的插件 ID。</summary>
    [SugarColumn(ColumnName = "plugin_id", IsPrimaryKey = true, Length = 128)]
    public string PluginId { get; set; } = string.Empty;

    /// <summary>插件内的业务键，如有人问词条 ID。</summary>
    [SugarColumn(ColumnName = "ref_key", IsPrimaryKey = true, Length = 256)]
    public string RefKey { get; set; } = string.Empty;

    /// <summary>业务对象所属的机器人账号；与账号无关时为 null。</summary>
    [SugarColumn(ColumnName = "self_id", IsNullable = true)]
    public long? SelfId { get; set; }

    /// <summary>创建时间。</summary>
    [SugarColumn(ColumnName = "created_at")]
    public DateTimeOffset CreatedAt { get; set; }
}
