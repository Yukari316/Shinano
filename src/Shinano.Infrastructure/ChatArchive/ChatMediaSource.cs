namespace Shinano.Infrastructure.ChatArchive;

/// <summary>
/// 获取一段媒体内容的来源信息，取自消息段，只在内存中使用，不写入数据库：临时链接很快失效。
/// </summary>
public sealed class ChatMediaSource
{
    /// <summary>媒体种类。</summary>
    public required ChatMediaKind Kind { get; init; }

    /// <summary>协议提供的临时下载链接。</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>发送方向的文件 URI，可能为 <c>base64://</c> 或 http(s) 链接。</summary>
    public string FileUri { get; init; } = string.Empty;

    /// <summary>资源 ID，临时链接缺失时用于换取下载链接。</summary>
    public string ResourceId { get; init; } = string.Empty;

    /// <summary>群文件或私聊文件 ID。</summary>
    public string FileId { get; init; } = string.Empty;

    /// <summary>私聊文件的 TriSHA1 哈希。</summary>
    public string FileHash { get; init; } = string.Empty;

    /// <summary>消息段声明的大小，单位字节；未知时为 null。</summary>
    public long? DeclaredSize { get; init; }
}
