using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Framework.Messaging;

/// <summary>按消息来源回复的便捷方法，直接调用事件参数的原生 API，发送结果由 Sora 记录。</summary>
public static class MessageReceivedEventExtensions
{
    /// <summary>向消息所在的群或私聊回复。</summary>
    /// <exception cref="NotSupportedException">消息来源既不是群聊也不是私聊。</exception>
    public static ValueTask<SendMessageResult> ReplyAsync(
        this MessageReceivedEvent e,
        MessageBody               body,
        CancellationToken         cancellationToken = default) =>
        e.Message.SourceType switch
        {
            MessageSourceType.Group  => e.Api.SendGroupMessageAsync(e.Message.GroupId, body, cancellationToken),
            MessageSourceType.Friend => e.Api.SendFriendMessageAsync(e.Message.SenderId, body, cancellationToken),
            MessageSourceType.Temp   => e.Api.SendFriendMessageAsync(e.Message.SenderId, body, cancellationToken),
            _ => throw new NotSupportedException($"不支持回复来源 {e.Message.SourceType}")
        };
}
