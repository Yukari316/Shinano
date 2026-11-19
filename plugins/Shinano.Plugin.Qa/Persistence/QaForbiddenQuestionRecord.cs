using SqlSugar;

namespace Shinano.Plugin.Qa.Persistence;

/// <summary>禁止提问词，位于全局 schema，对所有账号与群生效；录入时问题与条目完全相同即拒绝。</summary>
[SugarTable("qa_forbidden_question")]
[SugarIndex("ux_qa_forbidden_question", nameof(Question), OrderByType.Asc, true)]
public sealed class QaForbiddenQuestionRecord
{
    /// <summary>条目 ID。</summary>
    [SugarColumn(ColumnName = "id", IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>不能作为问题的文本。</summary>
    [SugarColumn(ColumnName = "question", Length = 200)]
    public string Question { get; set; } = string.Empty;
}
