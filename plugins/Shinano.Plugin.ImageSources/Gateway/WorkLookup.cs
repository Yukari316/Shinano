using System.Text.Json.Nodes;

namespace Shinano.Plugin.ImageSources.Gateway;

/// <summary>经图源服务查询作品信息的结果。</summary>
public sealed class WorkLookup
{
    /// <summary>作品存在时为上游原样的作品信息，不存在或查询失败时为 null。</summary>
    public JsonNode? Data { get; init; }

    /// <summary>作品不存在或已删除（区别于查询失败）。</summary>
    public bool NotFound { get; init; }
}
