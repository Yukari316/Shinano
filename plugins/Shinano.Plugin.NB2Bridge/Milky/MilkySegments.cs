using System.Text.Json.Nodes;
using Sora.Core.Enums;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.NB2Bridge.Milky;

/// <summary>
/// Sora 消息段与 Milky 消息段 JSON 的互相转换。入站方向（交给 NB2）只输出 Milky 已定义的段类型：官方适配器遇到未知类型会使整个事件解析失败。
/// 出站方向（NB2 要发送的消息）只接受 Milky 可发送的段类型，其余丢弃并由调用方记录。
/// </summary>
public static class MilkySegments
{
    private const string NORMAL  = "normal";
    private const string STICKER = "sticker";

    /// <summary>把收到的 Sora 消息转换为 Milky 入站消息段。</summary>
    public static JsonArray ToMilky(IEnumerable<Segment> body) => [.. body.Select(ToMilky).OfType<JsonObject>()];

    /// <summary>把 NB2 要发送的 Milky 出站消息段转换为 Sora 消息。</summary>
    /// <param name="segments">Milky 出站消息段。</param>
    /// <param name="dropped">无法转换而丢弃的段类型。</param>
    public static MessageBody FromMilky(JsonArray segments, ICollection<string> dropped)
    {
        MessageBody body = [];
        foreach (JsonObject segment in segments.OfType<JsonObject>())
        {
            string type = segment["type"]?.GetValue<string>() ?? string.Empty;
            JsonObject data = segment["data"] as JsonObject ?? [];
            if (FromMilky(type, data, dropped) is { } converted) body.Add(converted);
            else dropped.Add(type);
        }

        return body;
    }

    private static JsonObject? ToMilky(Segment segment) =>
        segment switch
        {
            TextSegment text       => Node("text", new JsonObject { ["text"] = text.Text }),
            MentionSegment mention => Node("mention", new JsonObject { ["user_id"] = (long)mention.Target, ["name"] = mention.Name }),
            MentionAllSegment      => Node("mention_all", []),
            FaceSegment face       => Node("face", new JsonObject { ["face_id"] = face.FaceId, ["is_large"] = face.IsLarge }),
            ReplySegment reply     => Reply(reply),
            ImageSegment image => Node(
                "image",
                new JsonObject
                {
                    ["resource_id"] = image.ResourceId,
                    ["temp_url"]    = image.Url,
                    ["width"]       = image.Width,
                    ["height"]      = image.Height,
                    ["summary"]     = image.Summary,
                    ["sub_type"]    = image.SubType == ImageSubType.Sticker ? STICKER : NORMAL
                }),
            AudioSegment audio => Node(
                "record",
                new JsonObject
                {
                    ["resource_id"] = audio.ResourceId,
                    ["temp_url"]    = audio.Url,
                    ["duration"]    = audio.Duration
                }),
            VideoSegment video => Node(
                "video",
                new JsonObject
                {
                    ["resource_id"] = video.ResourceId,
                    ["temp_url"]    = video.Url,
                    ["width"]       = video.Width,
                    ["height"]      = video.Height,
                    ["duration"]    = video.Duration
                }),
            FileSegment file => Node(
                "file",
                new JsonObject
                {
                    ["file_id"]   = file.FileId,
                    ["file_name"] = file.FileName,
                    ["file_size"] = file.FileSize,
                    ["file_hash"] = file.FileHash
                }),
            ForwardSegment forward => Node(
                "forward",
                new JsonObject
                {
                    ["forward_id"] = forward.ForwardId,
                    ["title"]      = forward.Title,
                    ["preview"]    = new JsonArray([.. forward.Preview.Select(line => (JsonNode)line)]),
                    ["summary"]    = forward.Summary
                }),
            MarketFaceSegment face => Node(
                "market_face",
                new JsonObject
                {
                    ["url"]              = face.Url,
                    ["emoji_package_id"] = face.EmojiPackageId,
                    ["emoji_id"]         = face.EmojiId,
                    ["key"]              = face.Key,
                    ["summary"]          = face.Summary
                }),
            LightAppSegment app => Node("light_app", new JsonObject { ["app_name"] = app.AppName, ["json_payload"] = app.JsonPayload }),
            XmlSegment xml      => Node("xml", new JsonObject { ["service_id"] = xml.ServiceId, ["xml_payload"] = xml.XmlPayload }),
            MarkdownSegment md  => Node("markdown", new JsonObject { ["content"] = md.Content }),
            _                   => null
        };

    // 引用未内嵌正文时只有消息序号；内嵌时适配器要求发送者、时间与正文齐全。
    private static JsonObject Reply(ReplySegment reply) =>
        reply.Content is { } content
            ? Node(
                "reply",
                new JsonObject
                {
                    ["message_seq"] = (long)reply.TargetId,
                    ["sender_id"]   = (long)reply.SenderId,
                    ["sender_name"] = reply.SenderName,
                    ["time"]        = reply.Time,
                    ["segments"]    = ToMilky(content)
                })
            : Node("reply", new JsonObject { ["message_seq"] = (long)reply.TargetId });

    private static JsonObject Node(string type, JsonObject data) =>
        new()
        {
            ["type"] = type,
            ["data"] = data
        };

    private static Segment? FromMilky(string type, JsonObject data, ICollection<string> dropped) =>
        type switch
        {
            "text"        => new TextSegment { Text = Text(data, "text") },
            "mention"     => new MentionSegment { Target = Long(data, "user_id") },
            "mention_all" => new MentionAllSegment(),
            "face" => new FaceSegment
            {
                FaceId  = Text(data, "face_id"),
                IsLarge = data["is_large"]?.GetValue<bool>() ?? false
            },
            "reply" => new ReplySegment { TargetId = Long(data, "message_seq") },
            "image" => new ImageSegment
            {
                FileUri = Text(data, "uri"),
                SubType = Text(data, "sub_type") == STICKER ? ImageSubType.Sticker : ImageSubType.Normal,
                Summary = Text(data, "summary")
            },
            "record" => new AudioSegment { FileUri = Text(data, "uri") },
            "video" => new VideoSegment
            {
                FileUri  = Text(data, "uri"),
                ThumbUri = Text(data, "thumb_uri")
            },
            "forward"   => Forward(data, dropped),
            "light_app" => new LightAppSegment { JsonPayload = Text(data, "json_payload") },
            _           => null
        };

    private static ForwardSegment Forward(JsonObject data, ICollection<string> dropped) =>
        new()
        {
            Messages =
            [
                .. (data["messages"] as JsonArray ?? []).OfType<JsonObject>()
                                                        .Select(message => new ForwardedMessageNode
                                                        {
                                                            UserId     = Long(message, "user_id"),
                                                            SenderName = Text(message, "sender_name"),
                                                            Time = message["time"]?.GetValue<long>() is { } time
                                                                ? DateTimeOffset.FromUnixTimeSeconds(time).LocalDateTime
                                                                : null,
                                                            Segments = FromMilky(message["segments"] as JsonArray ?? [], dropped)
                                                        })
            ],
            Title   = Text(data, "title"),
            Summary = Text(data, "summary"),
            Prompt  = Text(data, "prompt")
        };

    private static string Text(JsonObject data, string key) => data[key]?.GetValue<string>() ?? string.Empty;

    private static long Long(JsonObject data, string key) => data[key]?.GetValue<long>() ?? 0;
}
