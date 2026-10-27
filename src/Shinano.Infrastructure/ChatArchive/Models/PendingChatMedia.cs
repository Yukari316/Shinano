using System.Text.Json.Nodes;

namespace Shinano.Infrastructure.ChatArchive.Models;

/// <summary>等待归档的媒体：归档成功后在 <see cref="Node" /> 写入聊天媒体 ID <c>m</c>。</summary>
public sealed class PendingChatMedia
{
    /// <summary>段 JSON 中的媒体节点。</summary>
    public required JsonObject Node { get; init; }

    /// <summary>获取内容的来源。</summary>
    public required ChatMediaSource Source { get; init; }
}
