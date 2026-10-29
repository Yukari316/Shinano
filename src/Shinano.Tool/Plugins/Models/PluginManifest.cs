using System.Text.Json.Serialization;

namespace Shinano.Tool.Plugins.Models;

/// <summary>插件包根目录的 <c>plugin.json</c> 中工具用到的字段，与宿主的清单格式一致。</summary>
public sealed class PluginManifest
{
    /// <summary>清单文件名。</summary>
    public const string FILE_NAME = "plugin.json";

    /// <summary>插件 ID。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>版本。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;

    /// <summary>入口程序集。</summary>
    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>显示用的名称：未填写名称时取 ID。</summary>
    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}
