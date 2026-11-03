namespace Shinano.Framework.Rendering.Models;

/// <summary>排行中的一名。</summary>
public sealed class RankingEntry
{
    /// <summary>名称，过长时截断。</summary>
    public required string Name { get; init; }

    /// <summary>计数。</summary>
    public required long Count { get; init; }
}
