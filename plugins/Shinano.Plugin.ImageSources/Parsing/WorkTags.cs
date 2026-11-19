using System.Text.Json.Nodes;

namespace Shinano.Plugin.ImageSources.Parsing;

/// <summary>从作品信息中读取标签，按作品中的顺序，去掉重复。</summary>
public static class WorkTags
{
    private static readonly char[] Separators = [' '];

    /// <summary>Pixiv 作品（<c>illust</c> 对象）的标签：<c>tags[].name</c> 与中文翻译 <c>tags[].translated_name</c>。</summary>
    public static IReadOnlyList<WorkTag> FromPixiv(JsonObject illust) =>
    [
        .. (illust["tags"] as JsonArray ?? [])
           .Select(tag => JsonFields.Text(tag?["name"]) is { } name
               ? new WorkTag
               {
                   Name        = name,
                   Translation = JsonFields.Text(tag?["translated_name"])
               }
               : null)
           .OfType<WorkTag>()
           .DistinctBy(tag => tag.Name)
    ];

    /// <summary>
    /// 图站帖子的标签原值：Danbooru 为空格分隔的 <c>tag_string</c>，Moebooru（Yande.re）与 Gelbooru 为空格分隔的 <c>tags</c>，
    /// Sankaku 与 e-shuushuu 的 <c>tags</c> 为对象数组（取 <c>name</c>，没有时取 <c>title</c>）。
    /// </summary>
    public static IReadOnlyList<WorkTag> FromPost(JsonObject post)
    {
        IEnumerable<string> names = (post["tag_string"] ?? post["tags"]) switch
        {
            JsonArray items => items.Select(item => item is JsonObject tag ? JsonFields.Text(tag["name"]) ?? JsonFields.Text(tag["title"]) : JsonFields.Text(item))
                                    .OfType<string>(),
            { } text => (JsonFields.Text(text) ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries),
            null     => []
        };
        return [.. names.Distinct().Select(name => new WorkTag { Name = name })];
    }
}
