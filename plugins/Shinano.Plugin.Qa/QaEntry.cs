namespace Shinano.Plugin.Qa;

/// <summary>一条词条的只读快照。</summary>
public sealed class QaEntry
{
    /// <summary>词条 ID。</summary>
    public required long Id { get; init; }

    /// <summary>所属群。</summary>
    public required long GroupId { get; init; }

    /// <summary>匹配方式。</summary>
    public required QaMatchType MatchType { get; init; }

    /// <summary>问题文本或正则表达式。</summary>
    public required string Question { get; init; }

    /// <summary>回答片段。</summary>
    public required IReadOnlyList<QaAnswerPart> Answer { get; init; }

    /// <summary>本版本回答的媒体引用键后缀。</summary>
    public required string MediaKey { get; init; }

    /// <summary>录入人 QQ 号；面板录入为 0。</summary>
    public required long CreatedBy { get; init; }

    /// <summary>创建时间（UTC）；越新越优先。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>版本。</summary>
    public required int Version { get; init; }
}
