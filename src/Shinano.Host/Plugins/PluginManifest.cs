using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Shinano.Host.Plugins;

/// <summary>插件包根目录下 <c>plugin.json</c> 的内容。</summary>
public sealed partial class PluginManifest
{
    /// <summary>清单文件名。</summary>
    public const string FILE_NAME = "plugin.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling         = JsonCommentHandling.Skip
    };

    /// <summary>插件 ID：小写字母、数字与连字符，也用作表名前缀与调度器所有者。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>版本。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>描述。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>入口程序集文件名，该程序集是唯一登记到 Sora 的程序集。</summary>
    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>插件独立 bucket 的用途名，在全部插件间不得重复。</summary>
    [JsonPropertyName("bucketPurposes")]
    public string[] BucketPurposes { get; set; } = [];

    /// <summary>插件用到的全部数据表；表名须以 <see cref="TablePrefix" /> 开头。没有表时为空。</summary>
    [JsonPropertyName("tables")]
    public PluginTables Tables { get; set; } = new();

    /// <summary>插件表名的前缀：插件 ID 中的 <c>-</c> 换成 <c>_</c> 后加 <c>_</c>，如 <c>image-search</c> 为 <c>image_search_</c>。</summary>
    public static string TablePrefix(string pluginId) => pluginId.Replace('-', '_') + "_";

    /// <summary>读取并校验清单。</summary>
    /// <exception cref="InvalidDataException">清单缺少必填项或格式错误。</exception>
    public static PluginManifest Read(string path)
    {
        PluginManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} 不是有效的 JSON：{exception.Message}", exception);
        }

        if (manifest is null) throw new InvalidDataException($"{path} 为空");
        if (!IdPattern().IsMatch(manifest.Id)) throw new InvalidDataException($"{path} 的 id 无效：{manifest.Id}");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly)
            || Path.GetFileName(manifest.EntryAssembly) != manifest.EntryAssembly)
            throw new InvalidDataException($"{path} 的 entryAssembly 必须是插件目录下的文件名");
        CheckTables(path, manifest);
        return manifest;
    }

    // 清除插件数据时按声明删除表，表名必须属于本插件，否则会删掉其他插件的数据。
    private static void CheckTables(string path, PluginManifest manifest)
    {
        // JSON 中显式写 null 时反序列化结果为 null。
        if (manifest.Tables?.Global is null || manifest.Tables.Account is null)
            throw new InvalidDataException($"{path} 的 tables 及其 global、account 不能为 null");
        string prefix = TablePrefix(manifest.Id);
        string[] tables = [.. manifest.Tables.Global, .. manifest.Tables.Account];
        foreach (string table in tables)
            if (!TablePattern().IsMatch(table) || !table.StartsWith(prefix, StringComparison.Ordinal) || table.Length == prefix.Length)
                throw new InvalidDataException($"{path} 的表名 {table} 无效：须以 {prefix} 开头，只含小写字母、数字与下划线，不超过 63 个字符");
        if (tables.GroupBy(table => table, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1) is { } duplicate)
            throw new InvalidDataException($"{path} 的表名 {duplicate.Key} 重复声明");
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,62}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$")]
    private static partial Regex TablePattern();
}

/// <summary>
/// 插件在 <c>plugin.json</c> 中声明的数据表，写完整表名（不含 schema）。清除插件数据时只按这些表名全字匹配删除。
/// </summary>
public sealed class PluginTables
{
    /// <summary>位于全局 schema 的表。</summary>
    [JsonPropertyName("global")]
    public string[] Global { get; set; } = [];

    /// <summary>位于每个机器人账号 schema（<c>user_&lt;self_id&gt;</c>）的表。</summary>
    [JsonPropertyName("account")]
    public string[] Account { get; set; } = [];
}
