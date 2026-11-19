using System.Text.Json.Nodes;
using Sora.Core.Enums;
using Sora.Entities.Message;
using Sora.Entities.Recording;
using Sora.Entities.Segments;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>
/// 把 Sora 消息段编码为精简段 JSON，并收集需要后台归档的媒体与待展开的合并转发。
/// 一个实例只用于一条消息，不是线程安全的。
/// </summary>
/// <remarks>
/// 段 JSON 是数组，每段一个对象，<c>t</c> 为段类型：
/// <list type="bullet">
/// <item><c>text</c>：<c>v</c> 文本。</item>
/// <item><c>at</c>：<c>u</c> 被 @ 的 QQ 号；<c>atall</c>：@全体成员。不保存显示名。</item>
/// <item><c>face</c>：<c>id</c> 表情 ID，<c>big</c> 是否超级表情。</item>
/// <item><c>reply</c>：<c>mid</c> 被引用消息 ID，<c>u</c> 原发送者，<c>ts</c> 原发送时间（Unix 秒），<c>s</c> 被引用的内容。</item>
/// <item><c>img</c>：<c>sub</c> 为 <c>sticker</c> 表示表情包，<c>sum</c> 摘要。</item>
/// <item><c>mface</c>：商城表情，<c>pkg</c> 表情包 ID，<c>id</c> 表情 ID，<c>key</c>，<c>sum</c> 摘要。</item>
/// <item><c>audio</c>、<c>video</c>：<c>d</c> 时长（秒）；<c>file</c>：<c>n</c> 文件名，<c>z</c> 大小（字节）。</item>
/// <item><c>fwd</c>：<c>title</c>；<c>msgs</c> 内层消息数组，每项含 <c>u</c> 发送者（协议未提供时省略）、<c>n</c> 显示名、<c>ts</c> 时间、<c>s</c> 段；
/// 未展开时只有 <c>id</c> 转发 ID。</item>
/// <item><c>app</c>：<c>app</c> 应用名，<c>v</c> JSON 负载；<c>xml</c>：<c>sid</c> 服务 ID，<c>v</c> XML 负载；<c>md</c>：<c>v</c> 内容。</item>
/// </list>
/// 媒体类段（<c>img</c>、<c>mface</c>、<c>audio</c>、<c>video</c>、<c>file</c>）归档成功后带 <c>m</c>（聊天媒体 ID），否则缺失。
/// 合并转发内的转发节点显示名属于被转发的历史内容，予以保留。
/// </remarks>
public sealed class ChatSegmentEncoder
{
    /// <summary>合并转发展开的最大层数，更深的转发只保留转发 ID 并标记内容缺失。</summary>
    public const int MAX_FORWARD_DEPTH = 3;

    private const string STICKER = "sticker";

    private readonly List<PendingChatMedia>   _media    = [];
    private readonly List<PendingChatForward> _forwards = [];

    /// <summary>等待归档的媒体，展开转发时继续追加。</summary>
    public IReadOnlyList<PendingChatMedia> PendingMedia => _media;

    /// <summary>等待展开的合并转发，展开时可能继续追加更深一层的转发。</summary>
    public IReadOnlyList<PendingChatForward> PendingForwards => _forwards;

    /// <summary>编码时已确定有内容无法归档（转发超过最大层数或缺少内容与 ID）。</summary>
    public bool HasMissingContent { get; private set; }

    /// <summary>编码一组段。</summary>
    /// <param name="segments">消息段。</param>
    /// <param name="depth">所在的合并转发嵌套层级，消息顶层为 0。</param>
    public JsonArray Encode(IEnumerable<Segment> segments, int depth)
    {
        JsonArray array = [];
        foreach (Segment segment in segments)
            array.Add(EncodeSegment(segment, depth));
        return array;
    }

    /// <summary>编码经协议展开得到的合并转发内层消息。</summary>
    /// <param name="messages">内层消息。</param>
    /// <param name="depth">内层消息所在的层级。</param>
    public JsonArray EncodeForwardedMessages(IEnumerable<MessageContext> messages, int depth)
    {
        JsonArray array = [];
        foreach (MessageContext message in messages)
            array.Add(ForwardedMessage(message.SenderId, message.SenderName, message.Time, message.Body, depth));
        return array;
    }

    /// <summary>标记有内容无法归档。</summary>
    public void MarkMissing() => HasMissingContent = true;

    private JsonObject EncodeSegment(Segment segment, int depth) =>
        segment switch
        {
            TextSegment text => new JsonObject
            {
                ["t"] = "text",
                ["v"] = text.Text
            },
            MentionSegment mention => new JsonObject
            {
                ["t"] = "at",
                ["u"] = (long)mention.Target
            },
            MentionAllSegment => new JsonObject { ["t"] = "atall" },
            FaceSegment face => Face(face),
            ReplySegment reply => Reply(reply, depth),
            ImageSegment image => Image(image),
            MarketFaceSegment marketFace => MarketFace(marketFace),
            AudioSegment audio => Media(
                new JsonObject
                {
                    ["t"] = "audio",
                    ["d"] = audio.Duration
                },
                new ChatMediaSource
                {
                    Kind       = ChatMediaKind.Audio,
                    Url        = audio.Url,
                    FileUri    = audio.FileUri,
                    ResourceId = audio.ResourceId
                }),
            VideoSegment video => Media(
                new JsonObject
                {
                    ["t"] = "video",
                    ["d"] = video.Duration
                },
                new ChatMediaSource
                {
                    Kind       = ChatMediaKind.Video,
                    Url        = video.Url,
                    FileUri    = video.FileUri,
                    ResourceId = video.ResourceId
                }),
            FileSegment file => Media(
                new JsonObject
                {
                    ["t"] = "file",
                    ["n"] = file.FileName,
                    ["z"] = file.FileSize
                },
                new ChatMediaSource
                {
                    Kind         = ChatMediaKind.File,
                    FileId       = file.FileId,
                    FileHash     = file.FileHash,
                    DeclaredSize = file.FileSize > 0 ? file.FileSize : null
                }),
            ForwardRecordSegment forward => Forward(
                forward.ForwardId,
                forward.Title,
                [
                    .. forward.Messages.Select(message => new ForwardedContent
                    {
                        SenderId   = message.SenderId,
                        SenderName = message.SenderName,
                        Time       = message.Time,
                        Segments   = message.Segments
                    })
                ],
                depth),
            ForwardSegment forward => Forward(
                forward.ForwardId,
                forward.Title,
                [
                    .. forward.Messages.Select(message => new ForwardedContent
                    {
                        SenderId   = message.UserId,
                        SenderName = message.SenderName,
                        Time       = message.Time ?? default,
                        Segments   = message.Segments
                    })
                ],
                depth),
            LightAppSegment app => new JsonObject
            {
                ["t"]   = "app",
                ["app"] = app.AppName,
                ["v"]   = app.JsonPayload
            },
            XmlSegment xml => new JsonObject
            {
                ["t"]   = "xml",
                ["sid"] = xml.ServiceId,
                ["v"]   = xml.XmlPayload
            },
            MarkdownSegment markdown => new JsonObject
            {
                ["t"] = "md",
                ["v"] = markdown.Content
            },
            _ => new JsonObject { ["t"] = segment.Type.ToString().ToLowerInvariant() }
        };

    private static JsonObject Face(FaceSegment face)
    {
        JsonObject node = new()
        {
            ["t"]  = "face",
            ["id"] = face.FaceId
        };
        if (face.IsLarge) node["big"] = true;
        return node;
    }

    private JsonObject Reply(ReplySegment reply, int depth)
    {
        JsonObject node = new()
        {
            ["t"]   = "reply",
            ["mid"] = (long)reply.TargetId
        };
        if ((long)reply.SenderId != 0) node["u"] = (long)reply.SenderId;
        if (reply.Time > 0) node["ts"] = reply.Time;
        // 引用内容可能再含合并转发，按下一层计算以限制递归。
        if (reply.Content is { Count: > 0 } content) node["s"] = Encode(content, depth + 1);
        return node;
    }

    private JsonObject Image(ImageSegment image)
    {
        JsonObject node = new() { ["t"] = "img" };
        if (image.SubType == ImageSubType.Sticker) node["sub"] = STICKER;
        if (!string.IsNullOrEmpty(image.Summary)) node["sum"] = image.Summary;
        return Media(
            node,
            new ChatMediaSource
            {
                Kind       = ChatMediaKind.Image,
                Url        = image.Url,
                FileUri    = image.FileUri,
                ResourceId = image.ResourceId
            });
    }

    // 商城表情的摘要可能与图片内容不符，且同一表情会被大量重复使用，因此同样归档图片。
    private JsonObject MarketFace(MarketFaceSegment face) =>
        Media(
            new JsonObject
            {
                ["t"]   = "mface",
                ["pkg"] = face.EmojiPackageId,
                ["id"]  = face.EmojiId,
                ["key"] = face.Key,
                ["sum"] = face.Summary
            },
            new ChatMediaSource
            {
                Kind = ChatMediaKind.Image,
                Url  = face.Url
            });

    private JsonObject Media(JsonObject node, ChatMediaSource source)
    {
        _media.Add(
            new PendingChatMedia
            {
                Node   = node,
                Source = source
            });
        return node;
    }

    private JsonObject Forward(string forwardId, string title, IReadOnlyList<ForwardedContent> messages, int depth)
    {
        JsonObject node = new() { ["t"] = "fwd" };
        if (!string.IsNullOrEmpty(title)) node["title"] = title;

        if (depth >= MAX_FORWARD_DEPTH)
        {
            if (!string.IsNullOrEmpty(forwardId)) node["id"] = forwardId;
            MarkMissing();
            return node;
        }

        if (messages.Count > 0)
        {
            node["msgs"] = new JsonArray(
                [.. messages.Select(message => (JsonNode)ForwardedMessage(
                                        message.SenderId,
                                        message.SenderName,
                                        message.Time,
                                        message.Segments,
                                        depth + 1))]);
            return node;
        }

        if (string.IsNullOrEmpty(forwardId))
        {
            MarkMissing();
            return node;
        }

        node["id"] = forwardId;
        _forwards.Add(
            new PendingChatForward
            {
                Node      = node,
                ForwardId = forwardId,
                Depth     = depth
            });
        return node;
    }

    private JsonObject ForwardedMessage(
        long                 senderId,
        string               senderName,
        DateTime             time,
        IEnumerable<Segment> segments,
        int                  depth)
    {
        JsonObject node = new();
        // Milky 的转发内层消息不提供发送者 QQ 号，只有显示名。
        if (senderId != 0) node["u"] = senderId;
        if (!string.IsNullOrEmpty(senderName)) node["n"] = senderName;
        if (time != default) node["ts"] = new DateTimeOffset(time).ToUnixTimeSeconds();
        node["s"] = Encode(segments, depth);
        return node;
    }

    private sealed class ForwardedContent
    {
        public required long SenderId { get; init; }

        public required string SenderName { get; init; }

        public required DateTime Time { get; init; }

        public required IEnumerable<Segment> Segments { get; init; }
    }
}

/// <summary>等待归档的媒体：归档成功后在 <see cref="Node" /> 写入聊天媒体 ID <c>m</c>。</summary>
public sealed class PendingChatMedia
{
    /// <summary>段 JSON 中的媒体节点。</summary>
    public required JsonObject Node { get; init; }

    /// <summary>获取内容的来源。</summary>
    public required ChatMediaSource Source { get; init; }
}

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
