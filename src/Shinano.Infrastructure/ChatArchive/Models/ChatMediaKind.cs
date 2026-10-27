namespace Shinano.Infrastructure.ChatArchive.Models;

/// <summary>聊天媒体的种类，决定无法识别内容时的 MIME 回退值。</summary>
public enum ChatMediaKind
{
    /// <summary>图片、表情包与商城表情。</summary>
    Image,

    /// <summary>语音。</summary>
    Audio,

    /// <summary>视频。</summary>
    Video,

    /// <summary>文件。</summary>
    File
}
