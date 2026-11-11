using System.Text.Json.Nodes;

namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>NB2 插件配置表单：配置模型的 JSON Schema 与当前生效值。</summary>
public sealed class Nb2ConfigView
{
    /// <summary>pydantic 生成的 JSON Schema。</summary>
    public required JsonObject Schema { get; init; }

    /// <summary>当前生效值（含 <c>.env</c> 与默认值）。</summary>
    public required JsonObject Values { get; init; }
}
