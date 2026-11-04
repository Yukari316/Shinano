using Shinano.Plugin.Hso.Entities;

namespace Shinano.Plugin.Hso.Models;

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
