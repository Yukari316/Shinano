namespace Shinano.Plugin.ImageSources;

/// <summary>图站上的一个作品。</summary>
public sealed class SourceRef : IEquatable<SourceRef>
{
    /// <summary>图站。</summary>
    public required ImageSite Site { get; init; }

    /// <summary>作品号，按图站原样保存（Sankaku 新旧格式不同）。</summary>
    public required string Id { get; init; }

    /// <summary>页号，从 0 开始；只有 Pixiv 多页作品可能给出，未知时为 null。</summary>
    public int? Page { get; init; }

    /// <inheritdoc />
    public bool Equals(SourceRef? other) => other is not null && other.Site == Site && other.Id == Id;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as SourceRef);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Site, Id);
}
