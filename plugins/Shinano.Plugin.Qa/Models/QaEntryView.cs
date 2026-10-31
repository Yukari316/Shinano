namespace Shinano.Plugin.Qa.Models;

/// <summary>面板中的一条词条。</summary>
public sealed class QaEntryView
{
    /// <summary>词条 ID。</summary>
    public required long Id { get; init; }

    /// <summary>所属群。</summary>
    public required long GroupId { get; init; }

    /// <summary>匹配方式。</summary>
    public required QaMatchType MatchType { get; init; }

    /// <summary>问题文本或正则表达式。</summary>
    public required string Question { get; init; }

    /// <summary>回答片段；媒体经面板 API 读取。</summary>
    public required IReadOnlyList<QaAnswerPart> Answer { get; init; }

    /// <summary>录入人 QQ 号；面板录入为 0。</summary>
    public required long CreatedBy { get; init; }

    /// <summary>创建时间（UTC）。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>版本；面板确认覆盖时回传，用于判断是否已被他人修改。</summary>
    public required int Version { get; init; }

    /// <summary>由快照创建。</summary>
    public static QaEntryView From(QaEntry entry) =>
        new()
        {
            Id        = entry.Id,
            GroupId   = entry.GroupId,
            MatchType = entry.MatchType,
            Question  = entry.Question,
            Answer    = entry.Answer,
            CreatedBy = entry.CreatedBy,
            CreatedAt = entry.CreatedAt,
            Version   = entry.Version
        };
}
