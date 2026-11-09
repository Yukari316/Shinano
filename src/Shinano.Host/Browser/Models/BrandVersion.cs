namespace Shinano.Host.Browser.Models;

/// <summary>Client Hints 中的一个品牌。</summary>
public sealed class BrandVersion
{
    /// <summary>品牌名，如 <c>Chromium</c>。</summary>
    public required string Brand { get; init; }

    /// <summary>版本号。</summary>
    public required string Version { get; init; }
}
