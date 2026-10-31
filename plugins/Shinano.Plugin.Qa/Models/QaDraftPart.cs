using Sora.Entities.Segments;

namespace Shinano.Plugin.Qa.Models;

/// <summary>
/// 从聊天消息解析出、尚未保存的回答片段。媒体只带有取得内容所需的线索，保存为持久媒体后才成为 <see cref="QaAnswerPart" />。
/// </summary>
public sealed class QaDraftPart
{
    /// <summary>类型。</summary>
    public required QaPartKind Kind { get; init; }

    /// <summary>来源消息段，用于覆盖确认时原样展示文字、表情、@ 与图片。</summary>
    public required Segment Source { get; init; }

    /// <summary>文字内容，仅 <see cref="QaPartKind.Text" />。</summary>
    public string? Text { get; init; }

    /// <summary>QQ 表情 ID。</summary>
    public string? FaceId { get; init; }

    /// <summary>是否为大表情。</summary>
    public bool LargeFace { get; init; }

    /// <summary>被 @ 的 QQ 号。</summary>
    public long? UserId { get; init; }

    /// <summary>消息段携带的临时下载地址；为空时用 <see cref="ResourceId" /> 重新获取。</summary>
    public string? Url { get; init; }

    /// <summary>图片、语音、视频的资源 ID。</summary>
    public string? ResourceId { get; init; }

    /// <summary>群文件 ID，仅 <see cref="QaPartKind.File" />。</summary>
    public string? FileId { get; init; }

    /// <summary>文件名，仅 <see cref="QaPartKind.File" />。</summary>
    public string? FileName { get; init; }

    /// <summary>消息段声明的大小，未知时为 null。</summary>
    public long? DeclaredSize { get; init; }
}
