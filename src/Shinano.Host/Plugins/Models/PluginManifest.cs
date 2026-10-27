using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Shinano.Host.Plugins.Models;

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
        return manifest;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]{1,62}$")]
    private static partial Regex IdPattern();
}
