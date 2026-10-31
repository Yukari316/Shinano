namespace Shinano.Plugin.Qa.Models;

/// <summary>
/// 已保存的回答中的一段，按顺序组成完整回答，序列化为 JSON 存入词条。媒体只存持久媒体 ID，不存临时链接。
/// </summary>
public sealed class QaAnswerPart
{
    /// <summary>类型。</summary>
    public required QaPartKind Kind { get; init; }

    /// <summary>文字内容，仅 <see cref="QaPartKind.Text" />。</summary>
    public string? Text { get; init; }

    /// <summary>QQ 表情 ID，仅 <see cref="QaPartKind.Face" />。</summary>
    public string? FaceId { get; init; }

    /// <summary>是否为大表情，仅 <see cref="QaPartKind.Face" />。</summary>
    public bool LargeFace { get; init; }

    /// <summary>被 @ 的 QQ 号，仅 <see cref="QaPartKind.Mention" />。</summary>
    public long? UserId { get; init; }

    /// <summary>持久媒体 ID，仅图片、语音、视频与文件。</summary>
    public long? MediaId { get; init; }

    /// <summary>文件名，仅 <see cref="QaPartKind.File" />。</summary>
    public string? FileName { get; init; }
}
