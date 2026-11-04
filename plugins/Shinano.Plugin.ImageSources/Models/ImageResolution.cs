namespace Shinano.Plugin.ImageSources.Models;

/// <summary>按来源查询作品信息后，对原图的决定。</summary>
public sealed class ImageResolution
{
    /// <summary>要发送的原图；为 null 时只发卡片。</summary>
    public SourceRef? Image { get; init; }

    /// <summary>判定为 R-18，只发卡片。</summary>
    public bool Adult { get; init; }

    /// <summary>Pixiv 作品已被删除，且没有其他来源可用。</summary>
    public bool Deleted { get; init; }

    /// <summary>从 Pixiv 作品信息补充的标题；没有时为 null。</summary>
    public string? Title { get; init; }

    /// <summary>从作品信息补充的作者；没有时为 null。</summary>
    public string? Author { get; init; }

    /// <summary>Pixiv 作者的用户 ID；来源不是 Pixiv 时为 null。</summary>
    public long? AuthorId { get; init; }

    /// <summary>Pixiv 作品的标签（原文），按作品中的顺序；来源不是 Pixiv 时为空。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Pixiv 作品的页数；未知时为 null。</summary>
    public int? PageCount { get; init; }

    /// <summary>没有可用来源。</summary>
    public static ImageResolution None { get; } = new();
}
