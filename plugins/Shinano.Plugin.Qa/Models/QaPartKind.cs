namespace Shinano.Plugin.Qa.Models;

/// <summary>回答中一段内容的类型。</summary>
public enum QaPartKind
{
    /// <summary>文字。</summary>
    Text,

    /// <summary>QQ 表情。</summary>
    Face,

    /// <summary>@ 某个成员。</summary>
    Mention,

    /// <summary>图片。</summary>
    Image,

    /// <summary>语音。</summary>
    Audio,

    /// <summary>视频。</summary>
    Video,

    /// <summary>文件，以群文件发送。</summary>
    File
}
