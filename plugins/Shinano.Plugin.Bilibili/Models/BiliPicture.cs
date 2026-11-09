namespace Shinano.Plugin.Bilibili.Models;

/// <summary>动态中的一张图片。</summary>
public sealed class BiliPicture
{
    /// <summary>原图地址（https）。</summary>
    public required string Url { get; init; }

    /// <summary>宽度，单位像素；未知时为 0。</summary>
    public required int Width { get; init; }

    /// <summary>高度，单位像素；未知时为 0。</summary>
    public required int Height { get; init; }
}
