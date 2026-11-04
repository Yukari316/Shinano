using SqlSugar;

namespace Shinano.Plugin.Hso.Entities;

/// <summary>
/// 图库中的一张图片，位于全局 schema，切换账号后共享；对象存于插件 bucket，按 SHA256 去重。
/// <c>Shinano.Tool hso-import</c> 直接写入此表，表名与列名的变更须同步工具（由契约测试核对）。
/// </summary>
[SugarTable("hso_image")]
[SugarIndex("ux_hso_image_sha256", nameof(Sha256), OrderByType.Asc, true)]
[SugarIndex("ix_hso_image_pid", nameof(Pid), OrderByType.Asc, nameof(Page), OrderByType.Asc)]
public sealed class HsoImageRecord
{
    private const char TAG_SEPARATOR = '\n';

    /// <summary>图片 ID。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>插件 bucket 中的对象 key。</summary>
    [SugarColumn(ColumnName = "object_key", Length = 100)]
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>原始内容的 SHA256（小写十六进制），用于去重。</summary>
    [SugarColumn(ColumnName = "sha256", Length = 64)]
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>MIME 类型。</summary>
    [SugarColumn(ColumnName = "media_type", Length = 50)]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>大小，单位字节。</summary>
    [SugarColumn(ColumnName = "size")]
    public long Size { get; set; }

    /// <summary>导入时的文件名；<c>AD</c> 导入为 null。</summary>
    [SugarColumn(ColumnName = "file_name", Length = 255, IsNullable = true)]
    public string? FileName { get; set; }

    /// <summary>Pixiv 作品号；未知时为 null。</summary>
    [SugarColumn(ColumnName = "pid", IsNullable = true)]
    public long? Pid { get; set; }

    /// <summary>Pixiv 页号，从 0 开始；未知时为 null。</summary>
    [SugarColumn(ColumnName = "page", IsNullable = true)]
    public int? Page { get; set; }

    /// <summary>作品标题。</summary>
    [SugarColumn(ColumnName = "title", Length = 500, IsNullable = true)]
    public string? Title { get; set; }

    /// <summary>作者。</summary>
    [SugarColumn(ColumnName = "author", Length = 200, IsNullable = true)]
    public string? Author { get; set; }

    /// <summary>Pixiv 作者的用户 ID；来源不是 Pixiv 或未知时为 null。</summary>
    [SugarColumn(ColumnName = "author_id", IsNullable = true)]
    public long? AuthorId { get; set; }

    /// <summary>Pixiv 标签（原文），以换行分隔（标签不含换行）；没有时为 null。读写用 <see cref="TagList" />。</summary>
    [SugarColumn(ColumnName = "tags", ColumnDataType = "text", IsNullable = true)]
    public string? Tags { get; set; }

    /// <summary><see cref="Tags" /> 的列表形式。</summary>
    [SugarColumn(IsIgnore = true)]
    public IReadOnlyList<string> TagList
    {
        get => Tags is null ? [] : Tags.Split(TAG_SEPARATOR);
        set => Tags = value.Count == 0 ? null : string.Join(TAG_SEPARATOR, value);
    }

    /// <summary>作品信息的来源站名，如 Pixiv、Danbooru。</summary>
    [SugarColumn(ColumnName = "source_name", Length = 50, IsNullable = true)]
    public string? SourceName { get; set; }

    /// <summary>是否为 NSFW。</summary>
    [SugarColumn(ColumnName = "nsfw")]
    public bool Nsfw { get; set; }

    /// <summary>管理员是否在面板手动设置过 NSFW；设置过的不被补全的作品信息覆盖。</summary>
    [SugarColumn(ColumnName = "nsfw_manual")]
    public bool NsfwManual { get; set; }

    /// <summary>是否被腾讯拦截过：之后每次发送都先改像素。</summary>
    [SugarColumn(ColumnName = "blocked")]
    public bool Blocked { get; set; }

    /// <summary>作品信息的补全状态（<see cref="Models.HsoInfoState" /> 的数值）。</summary>
    [SugarColumn(ColumnName = "info_state")]
    public int InfoState { get; set; }

    /// <summary>导入方式（<see cref="Models.HsoImageOrigin" /> 的数值）。</summary>
    [SugarColumn(ColumnName = "origin")]
    public int Origin { get; set; }

    /// <summary>导入时间（UTC）。</summary>
    [SugarColumn(ColumnName = "imported_at")]
    public DateTimeOffset ImportedAt { get; set; }
}
