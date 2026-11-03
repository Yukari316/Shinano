namespace Shinano.Framework.Rendering.Models;

/// <summary>列表中的一项。</summary>
public sealed class ListCardItem
{
    /// <summary>序号，跨页连续由调用方给出。</summary>
    public required int Index { get; init; }

    /// <summary>正文前的短标签，如 <c>RG</c>；没有时为 null。</summary>
    public string? Tag { get; init; }

    /// <summary>正文，过长时换行。</summary>
    public required string Text { get; init; }
}
