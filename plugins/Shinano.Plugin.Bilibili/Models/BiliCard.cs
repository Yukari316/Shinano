namespace Shinano.Plugin.Bilibili.Models;

/// <summary>一条动态绘制好的卡片。</summary>
public sealed class BiliCard
{
    /// <summary>PNG 字节。</summary>
    public required byte[] Png { get; init; }

    /// <summary>
    /// 正文被截断（专栏总是视为截断）时附加原文链接指向的动态 ID：本动态被截断时为本动态，只有转发的原动态被截断时为原动态；
    /// 没有截断时为 null。
    /// </summary>
    public string? MoreLinkId { get; init; }
}
