using System.Text.Json.Nodes;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>只有转发 ID 的合并转发：展开成功后在 <see cref="Node" /> 写入内层消息 <c>msgs</c>。</summary>
public sealed class PendingChatForward
{
    /// <summary>段 JSON 中的转发节点。</summary>
    public required JsonObject Node { get; init; }

    /// <summary>转发 ID。</summary>
    public required string ForwardId { get; init; }

    /// <summary>转发节点所在的嵌套层级，消息顶层为 0。</summary>
    public required int Depth { get; init; }
}
