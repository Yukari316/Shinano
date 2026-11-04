namespace Shinano.Plugin.Hso.Models;

/// <summary>面板图库的一页。</summary>
public sealed class HsoImagePage
{
    /// <summary>符合条件的总张数。</summary>
    public required int Total { get; init; }

    /// <summary>本页图片。</summary>
    public required IReadOnlyList<HsoImageView> Items { get; init; }
}
