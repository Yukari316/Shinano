using System.Text.Json.Nodes;

namespace Shinano.Host.Plugins.Config;

/// <summary>面板通用配置表单的内容。</summary>
public sealed class PluginConfigView
{
    /// <summary>配置类型的 JSON Schema。</summary>
    public required JsonNode Schema { get; init; }

    /// <summary>当前配置值。</summary>
    public required JsonNode? Value { get; init; }
}
