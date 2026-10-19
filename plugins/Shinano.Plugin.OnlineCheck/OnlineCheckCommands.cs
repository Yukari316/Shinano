using Shinano.Framework.Messaging;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.Segments;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.OnlineCheck;

/// <summary>私聊"在"与 Super User 的群内 echo。</summary>
[CommandGroup(Name = "online-check")]
public sealed class OnlineCheckCommands
{
    private const string ECHO_PREFIX = "echo";

    private readonly OnlineCheckAssets _assets;

    /// <summary>创建指令组。</summary>
    public OnlineCheckCommands(OnlineCheckAssets assets)
    {
        _assets = assets;
    }

    /// <summary>好友私聊"在"时回复固定图片。</summary>
    [Command(
        Expressions = ["在"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Friend,
        Description = "确认 Bot 在线")]
    public async ValueTask PresenceAsync(MessageReceivedEvent e) =>
        await e.ReplyAsync(SegmentBuilder.Image(_assets.OnlineImageUri));

    /// <summary>群内复读 <c>echo </c> 之后的全部内容，包括图片等消息段。</summary>
    [Command(
        Expressions = [@"^echo\s[\s\S]+$"],
        MatchType = MatchType.Regex,
        SourceScope = MessageSourceScope.Group,
        SuperUserOnly = true,
        Description = "复读消息")]
    public async ValueTask EchoAsync(MessageReceivedEvent e)
    {
        MessageBody body = StripEchoPrefix(e.Message.Body);
        if (body.Count > 0) await e.ReplyAsync(body);
    }

    /// <summary>去掉首个文本段开头的 <c>echo</c> 与其后一个空白字符；去掉后为空的文本段一并移除。</summary>
    public static MessageBody StripEchoPrefix(MessageBody source)
    {
        MessageBody body = [.. source];
        if (body.Count == 0 || body[0] is not TextSegment first || !first.Text.StartsWith(ECHO_PREFIX, StringComparison.Ordinal))
            return body;

        string rest = first.Text[(ECHO_PREFIX.Length + 1)..];
        if (rest.Length == 0) body.RemoveAt(0);
        else body[0] = first with { Text = rest };
        return body;
    }
}
