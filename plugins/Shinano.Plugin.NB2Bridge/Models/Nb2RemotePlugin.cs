using System.Text.Json.Nodes;

namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>控制插件 <c>GET /shinano/plugins</c> 返回的一项：NB2 进程内该插件的实时状态。</summary>
public sealed class Nb2RemotePlugin
{
    /// <summary>模块名。</summary>
    public required string Module { get; init; }

    /// <summary>是否加载成功。</summary>
    public required bool Loaded { get; init; }

    /// <summary>NB2 内是否响应事件。</summary>
    public required bool Enabled { get; init; }

    /// <summary><c>PluginMetadata.name</c>；没有元数据时为模块名。</summary>
    public required string Name { get; init; }

    /// <summary><c>PluginMetadata.description</c>。</summary>
    public required string Description { get; init; }

    /// <summary>当前登记的 matcher 数（含临时 matcher）。</summary>
    public required int Matchers { get; init; }

    /// <summary><c>PluginMetadata.config</c> 的 JSON Schema；未声明配置模型时为 null。</summary>
    public JsonObject? ConfigSchema { get; init; }

    /// <summary>配置的当前生效值；未声明配置模型时为 null。</summary>
    public JsonObject? Config { get; init; }

    /// <summary>进程启动以来的统计。</summary>
    public required Nb2PluginStats Stats { get; init; }
}
