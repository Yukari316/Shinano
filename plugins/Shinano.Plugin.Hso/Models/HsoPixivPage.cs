namespace Shinano.Plugin.Hso.Models;

/// <summary>Pixiv 作品的一页。</summary>
public sealed class HsoPixivPage
{
    /// <summary>作品号。</summary>
    public required long Pid { get; init; }

    /// <summary>页号，从 0 开始。</summary>
    public required int Page { get; init; }
}
