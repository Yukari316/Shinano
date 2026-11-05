namespace Shinano.Plugin.Hso.Models;

/// <summary>否决审核时管理员手动补全的作品信息；各项都可留空，全部留空时记为查无信息。</summary>
public sealed class HsoManualInfo
{
    /// <summary>标题。</summary>
    public string? Title { get; init; }

    /// <summary>作者。</summary>
    public string? Author { get; init; }

    /// <summary>Pixiv 作者 ID。</summary>
    public long? AuthorId { get; init; }

    /// <summary>Pixiv 作品号。</summary>
    public long? Pid { get; init; }

    /// <summary>Pixiv 页号，从 0 开始。</summary>
    public int? Page { get; init; }

    /// <summary>标签。</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>是否为 NSFW；设为 true 时记为管理员手动设置。</summary>
    public bool Nsfw { get; init; }
}
