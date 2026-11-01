namespace Shinano.Plugin.ImageSearch.Models;

/// <summary>要搜索的图片。</summary>
public sealed class QueryImage
{
    /// <summary>图片内容。</summary>
    public required byte[] Content { get; init; }

    /// <summary>MIME 类型。</summary>
    public required string MediaType { get; init; }
}
