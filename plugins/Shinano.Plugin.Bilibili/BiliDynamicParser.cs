using System.Globalization;
using System.Text.Json;
using Shinano.Plugin.Bilibili.Models;

namespace Shinano.Plugin.Bilibili;

/// <summary>
/// 把 B站 Web 动态接口（<c>x/polymer/web-dynamic/v1/feed/*</c>，<c>features=itemOpusStyle</c>）的条目解析为卡片所需的内容。
/// 字段缺失时取空值而不抛出：接口字段随 B站改版变化，单条动态解析不完整不应影响其他动态。
/// </summary>
public static class BiliDynamicParser
{
    private const string HTTP_PREFIX       = "http://";
    private const string HTTPS_PREFIX      = "https://";
    private const string PROTOCOL_RELATIVE = "//";
    private const string TYPE_NONE         = "DYNAMIC_TYPE_NONE";

    private static readonly Dictionary<string, string> AttachmentNames = new()
    {
        ["ADDITIONAL_TYPE_RESERVE"]        = "预约",
        ["ADDITIONAL_TYPE_VOTE"]           = "投票",
        ["ADDITIONAL_TYPE_GOODS"]          = "商品",
        ["ADDITIONAL_TYPE_UGC"]            = "视频",
        ["ADDITIONAL_TYPE_COMMON"]         = "相关",
        ["ADDITIONAL_TYPE_MATCH"]          = "赛事",
        ["ADDITIONAL_TYPE_UPOWER_LOTTERY"] = "抽奖"
    };

    /// <summary>解析一页动态流（<c>data</c> 对象）。</summary>
    public static BiliFeedPage ParsePage(JsonElement data)
    {
        List<BiliDynamic> items = [];
        if (Child(data, "items") is { ValueKind: JsonValueKind.Array } array)
            items.AddRange(array.EnumerateArray().Select(Parse).OfType<BiliDynamic>());

        bool hasMore = Child(data, "has_more").ValueKind == JsonValueKind.True;
        string offset = String(data, "offset");
        string baseline = String(data, "update_baseline");
        return new BiliFeedPage
        {
            Items          = items,
            NextOffset     = hasMore && offset.Length > 0 ? offset : null,
            UpdateBaseline = baseline.Length > 0 ? baseline : null
        };
    }

    /// <summary>解析一条动态；已删除（<c>DYNAMIC_TYPE_NONE</c>）或没有 ID 时为 null。</summary>
    public static BiliDynamic? Parse(JsonElement item)
    {
        string id = String(item, "id_str");
        string type = String(item, "type");
        if (id.Length == 0 || type == TYPE_NONE) return null;

        JsonElement modules = Child(item, "modules");
        JsonElement author = Child(modules, "module_author");
        JsonElement dynamic = Child(modules, "module_dynamic");
        JsonElement major = Child(dynamic, "major");
        JsonElement opus = Child(major, "opus");
        BiliDynamicKind kind = KindOf(type);

        IReadOnlyList<BiliText> text = kind switch
        {
            BiliDynamicKind.Opus or BiliDynamicKind.Article when opus.ValueKind == JsonValueKind.Object => Rich(Child(opus, "summary")),
            _                                                                                         => Rich(Child(dynamic, "desc"))
        };

        return new BiliDynamic
        {
            Id          = id,
            Kind        = kind,
            AuthorId    = Long(author, "mid"),
            AuthorName  = String(author, "name"),
            AuthorFace  = Https(String(author, "face")),
            PublishedAt = DateTimeOffset.FromUnixTimeSeconds(Long(author, "pub_ts")),
            Action      = Action(kind, String(author, "pub_action")),
            Pinned      = String(Child(modules, "module_tag"), "text").Length > 0,
            Title       = Title(kind, major, opus),
            Text        = text.Count > 0 || kind != BiliDynamicKind.Generic ? text : GenericText(major),
            Pictures    = Pictures(major, opus),
            Video       = kind == BiliDynamicKind.Video ? Video(Child(major, "archive")) : null,
            Attachment  = Attachment(Child(dynamic, "additional")),
            Original    = kind == BiliDynamicKind.Forward && Child(item, "orig") is { ValueKind: JsonValueKind.Object } original ? Parse(original) : null
        };
    }

    private static BiliDynamicKind KindOf(string type) =>
        type switch
        {
            "DYNAMIC_TYPE_DRAW" or "DYNAMIC_TYPE_WORD" => BiliDynamicKind.Opus,
            "DYNAMIC_TYPE_ARTICLE"                     => BiliDynamicKind.Article,
            "DYNAMIC_TYPE_AV"                          => BiliDynamicKind.Video,
            "DYNAMIC_TYPE_FORWARD"                     => BiliDynamicKind.Forward,
            "DYNAMIC_TYPE_LIVE_RCMD"                   => BiliDynamicKind.LiveRecommend,
            _                                          => BiliDynamicKind.Generic
        };

    // 图文与转发的 pub_action 为空，按类型补上与 B站页面一致的动作文字。
    private static string Action(BiliDynamicKind kind, string action) =>
        action.Length > 0
            ? action
            : kind switch
            {
                BiliDynamicKind.Forward => "转发了动态",
                BiliDynamicKind.Video   => "投稿了视频",
                BiliDynamicKind.Article => "投稿了文章",
                _                       => "发布了动态"
            };

    private static string? Title(BiliDynamicKind kind, JsonElement major, JsonElement opus)
    {
        string title = kind switch
        {
            BiliDynamicKind.Opus or BiliDynamicKind.Article => String(opus, "title"),
            BiliDynamicKind.Generic                         => MajorField(major, "title"),
            _                                               => ""
        };
        return title.Length > 0 ? title : null;
    }

    // 通用类型（音频、番剧、直播等）没有 desc 时取 major 内对象的简介字段。
    private static IReadOnlyList<BiliText> GenericText(JsonElement major)
    {
        string text = MajorField(major, "desc");
        return text.Length > 0 ? [new BiliText { Kind = BiliTextKind.Plain, Text = text }] : [];
    }

    // major 的有效内容放在与 type 对应的子对象中（如 MAJOR_TYPE_COMMON 对应 common），其余子对象为 null。
    private static string MajorField(JsonElement major, string field)
    {
        if (major.ValueKind != JsonValueKind.Object) return "";
        foreach (JsonProperty property in major.EnumerateObject())
            if (property.Value.ValueKind == JsonValueKind.Object && String(property.Value, field) is { Length: > 0 } value)
                return value;
        return "";
    }

    private static IReadOnlyList<BiliText> Rich(JsonElement container)
    {
        if (Child(container, "rich_text_nodes") is not { ValueKind: JsonValueKind.Array } nodes)
        {
            string plain = String(container, "text");
            return plain.Length > 0 ? [new BiliText { Kind = BiliTextKind.Plain, Text = plain }] : [];
        }

        return
        [
            .. nodes.EnumerateArray()
                    .Select(node => new BiliText
                    {
                        Kind = String(node, "type") switch
                        {
                            "RICH_TEXT_NODE_TYPE_TEXT"  => BiliTextKind.Plain,
                            "RICH_TEXT_NODE_TYPE_EMOJI" => BiliTextKind.Emoji,
                            _                           => BiliTextKind.Highlight
                        },
                        Text = String(node, "text")
                    })
                    .Where(node => node.Text.Length > 0)
        ];
    }

    private static IReadOnlyList<BiliPicture> Pictures(JsonElement major, JsonElement opus)
    {
        if (Child(opus, "pics") is { ValueKind: JsonValueKind.Array } pics)
            return [.. pics.EnumerateArray().Select(pic => Picture(pic, "url")).OfType<BiliPicture>()];

        // 不带 itemOpusStyle 的旧形态：图片在 major.draw.items。
        if (Child(Child(major, "draw"), "items") is { ValueKind: JsonValueKind.Array } items)
            return [.. items.EnumerateArray().Select(pic => Picture(pic, "src")).OfType<BiliPicture>()];

        return [];
    }

    private static BiliPicture? Picture(JsonElement pic, string urlField)
    {
        string url = Https(String(pic, urlField));
        return url.Length == 0
            ? null
            : new BiliPicture
            {
                Url    = url,
                Width  = (int)Long(pic, "width"),
                Height = (int)Long(pic, "height")
            };
    }

    private static BiliVideo? Video(JsonElement archive) =>
        archive.ValueKind != JsonValueKind.Object
            ? null
            : new BiliVideo
            {
                Bvid        = String(archive, "bvid"),
                Cover       = Https(String(archive, "cover")),
                Title       = String(archive, "title"),
                Description = String(archive, "desc"),
                Duration    = String(archive, "duration_text")
            };

    private static string? Attachment(JsonElement additional)
    {
        string type = String(additional, "type");
        if (type.Length == 0) return null;

        // 附加卡片的内容放在与类型对应的子对象中，标题字段名不统一（预约为 title，投票为 desc）。
        string title = "";
        foreach (JsonProperty property in additional.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object) continue;
            title = String(property.Value, "title") is { Length: > 0 } named ? named : String(property.Value, "desc");
            if (title.Length > 0) break;
        }

        if (title.Length == 0) return null;
        return AttachmentNames.TryGetValue(type, out string? name) ? $"{name}：{title}" : title;
    }

    private static string Https(string url) =>
        url.StartsWith(HTTP_PREFIX, StringComparison.Ordinal) ? HTTPS_PREFIX + url[HTTP_PREFIX.Length..]
        : url.StartsWith(PROTOCOL_RELATIVE, StringComparison.Ordinal) ? "https:" + url
        : url;

    private static JsonElement Child(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement child) ? child : default;

    private static string String(JsonElement element, string name) =>
        Child(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() ?? "" : "";

    // B站的数字字段有时为字符串（如 pub_ts、mid 的不同版本），两种都接受。
    private static long Long(JsonElement element, string name) =>
        Child(element, name) switch
        {
            { ValueKind: JsonValueKind.Number } number when number.TryGetInt64(out long value) => value,
            { ValueKind: JsonValueKind.String } text when long.TryParse(text.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) => value,
            _ => 0
        };
}
