using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shinano.Plugin.ImageSearch.Parsing;

/// <summary>读取上游 JSON 字段：同一字段在不同服务或条目中可能是字符串、数字或数组。</summary>
internal static class JsonFields
{
    /// <summary>字段的文本形式；数组以「、」连接；缺失、为 null 或为空时返回 null。</summary>
    public static string? Text(JsonNode? node) =>
        node switch
        {
            JsonValue value when value.GetValueKind() == JsonValueKind.String => NullIfEmpty(value.GetValue<string>()),
            JsonValue value when value.GetValueKind() == JsonValueKind.Number => value.ToJsonString(),
            JsonArray items => NullIfEmpty(string.Join("、", items.Select(Text).OfType<string>())),
            _ => null
        };

    /// <summary>字段的数值；字符串形式的数字同样接受；无法解析时返回 null。</summary>
    public static double? Number(JsonNode? node) =>
        Text(node) is { } text && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : null;

    private static string? NullIfEmpty(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
