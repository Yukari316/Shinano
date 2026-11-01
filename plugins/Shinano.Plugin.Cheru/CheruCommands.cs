using Shinano.Framework.Commands;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.Segments;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.Cheru;

/// <summary>
/// "切噜一下"编码与"切噜~"解码。只处理纯文本消息；带图片等其他消息段时不回复，也不占用这条消息。
/// </summary>
[CommandGroup(Name = "cheru")]
public sealed class CheruCommands
{
    private const string ENCODE_PREFIX = "切噜一下";
    private const string DECODE_PREFIX = "切噜~";
    private const string ENCODED_REPLY = "切噜～";
    private const string DECODED_REPLY = "切噜的意思是:";

    /// <summary>把文本编码为切噜语。</summary>
    [Command(
        Expressions = [$"^{ENCODE_PREFIX}[\\s\\S]+"],
        MatchType = MatchType.Regex,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        BlockAfterMatch = false,
        Description = "切噜一下")]
    public async ValueTask EncodeAsync(MessageReceivedEvent e)
    {
        if (PlainTextAfter(e.Message.Body, ENCODE_PREFIX.Length) is not { } text) return;
        e.IsContinueEventChain = false;
        await e.ReplyAsync(new MessageBody(ENCODED_REPLY + CheruCodec.Encode(text)));
    }

    /// <summary>把切噜语解码为原文；前缀中的波浪号可以是半角或全角。</summary>
    [Command(
        Expressions = ["^切噜[~～][\\s\\S]+"],
        MatchType = MatchType.Regex,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        BlockAfterMatch = false,
        Description = "切噜翻译")]
    public async ValueTask DecodeAsync(MessageReceivedEvent e)
    {
        if (PlainTextAfter(e.Message.Body, DECODE_PREFIX.Length) is not { } text) return;
        e.IsContinueEventChain = false;
        await e.ReplyAsync(new MessageBody(DECODED_REPLY + CheruCodec.Decode(text)));
    }

    // 指令按去掉首尾空白的文字匹配，这里同样去掉首尾空白后再截掉前缀。
    private static string? PlainTextAfter(MessageBody body, int prefixLength)
    {
        if (body.Count == 0 || body.Any(segment => segment is not TextSegment)) return null;
        string text = body.GetText().Trim();
        return text.Length > prefixLength ? text[prefixLength..] : null;
    }
}
