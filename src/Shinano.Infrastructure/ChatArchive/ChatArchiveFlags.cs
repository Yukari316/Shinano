namespace Shinano.Infrastructure.ChatArchive;

/// <summary>聊天记录的归档状态位，以 smallint 存储，位值不可更改。</summary>
[Flags]
public enum ChatArchiveFlags : short
{
    /// <summary>正文与媒体完整。</summary>
    None = 0,

    /// <summary>媒体或合并转发内容仍在后台归档中。</summary>
    MediaPending = 1,

    /// <summary>
    /// 有媒体或合并转发内容未能归档（超限、下载或上传失败、转发无法展开、处理被进程停止打断）；
    /// 段 JSON 中对应媒体缺少 <c>m</c>，未展开的转发缺少 <c>msgs</c>。
    /// </summary>
    ContentMissing = 2
}
