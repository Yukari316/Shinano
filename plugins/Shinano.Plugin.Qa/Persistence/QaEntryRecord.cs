using SqlSugar;

namespace Shinano.Plugin.Qa.Persistence;

/// <summary>有人问词条，位于机器人账号 schema，按群隔离；同群同匹配方式的问题唯一。</summary>
[SugarTable("qa_entry")]
[SugarIndex(
    "ux_qa_entry_question",
    nameof(GroupId),
    OrderByType.Asc,
    nameof(MatchType),
    OrderByType.Asc,
    nameof(Question),
    OrderByType.Asc,
    true)]
public sealed class QaEntryRecord
{
    /// <summary>词条 ID。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>所属群。</summary>
    [SugarColumn(ColumnName = "group_id")]
    public long GroupId { get; set; }

    /// <summary>匹配方式（<see cref="QaMatchType" /> 的数值）。</summary>
    [SugarColumn(ColumnName = "match_type")]
    public int MatchType { get; set; }

    /// <summary>问题文本或正则表达式。</summary>
    [SugarColumn(ColumnName = "question", Length = 200)]
    public string Question { get; set; } = string.Empty;

    /// <summary>回答：按顺序的片段 JSON，媒体只存持久媒体 ID。</summary>
    [SugarColumn(ColumnName = "answer", ColumnDataType = "text")]
    public string Answer { get; set; } = string.Empty;

    /// <summary>本版本回答的媒体引用键后缀；覆盖时更换，旧版本的引用随之删除。</summary>
    [SugarColumn(ColumnName = "media_key", Length = 32)]
    public string MediaKey { get; set; } = string.Empty;

    /// <summary>录入人 QQ 号；面板录入为 0。</summary>
    [SugarColumn(ColumnName = "created_by")]
    public long CreatedBy { get; set; }

    /// <summary>创建时间（UTC），覆盖时更新；越新越优先。</summary>
    [SugarColumn(ColumnName = "created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>版本，每次覆盖加一，用于判断确认期间是否被他人修改。</summary>
    [SugarColumn(ColumnName = "version")]
    public int Version { get; set; }
}
