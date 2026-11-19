using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Shinano.Plugin.NB2Bridge.PluginPackages;

/// <summary>
/// 写入 NB2 的 <c>.env</c>：字段名转大写作为键，保留其他行与注释。NoneBot 用 python-dotenv 读取，双引号值中的
/// <c>\\</c>、<c>\"</c>、<c>\n</c> 会被还原；列表与对象写成带引号的 JSON，由 NoneBot 按字段类型解析。
/// </summary>
public static class Nb2DotEnv
{
    private const string EXPORT_PREFIX = "export ";

    /// <summary>设置若干字段；值为 null 时删除该键（恢复插件默认值）。</summary>
    public static void Save(string path, IReadOnlyDictionary<string, JsonNode?> values)
    {
        List<string> lines = File.Exists(path) ? [.. File.ReadAllLines(path)] : [];
        foreach ((string field, JsonNode? value) in values)
        {
            string key = field.ToUpperInvariant();
            int index = lines.FindIndex(line => KeyOf(line) == key);
            lines.RemoveAll(line => KeyOf(line) == key);
            if (value is null) continue;

            string line = $"{key}={Encode(value)}";
            if (index < 0 || index > lines.Count) lines.Add(line);
            else lines.Insert(index, line);
        }

        string temporary = path + ".tmp";
        File.WriteAllLines(temporary, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>把 JSON 值编码为 dotenv 值。</summary>
    public static string Encode(JsonNode value) =>
        value.GetValueKind() switch
        {
            JsonValueKind.True   => "true",
            JsonValueKind.False  => "false",
            JsonValueKind.Number => value.ToJsonString(),
            JsonValueKind.String => Quote(value.GetValue<string>()),
            _                    => Quote(value.ToJsonString())
        };

    private static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal)
                   .Replace("\"", "\\\"", StringComparison.Ordinal)
                   .Replace("\n", "\\n", StringComparison.Ordinal) + "\"";

    private static string? KeyOf(string line)
    {
        string trimmed = line.TrimStart();
        if (trimmed.StartsWith('#')) return null;
        if (trimmed.StartsWith(EXPORT_PREFIX, StringComparison.Ordinal)) trimmed = trimmed[EXPORT_PREFIX.Length..].TrimStart();
        int equals = trimmed.IndexOf('=', StringComparison.Ordinal);
        return equals <= 0 ? null : trimmed[..equals].Trim().ToUpper(CultureInfo.InvariantCulture);
    }
}
