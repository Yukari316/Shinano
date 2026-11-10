using System.Text.Json.Serialization;

namespace Shinano.Host.Plugins.Models;

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
