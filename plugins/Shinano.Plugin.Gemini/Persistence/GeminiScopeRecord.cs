using SqlSugar;

namespace Shinano.Plugin.Gemini.Persistence;

/// <summary>一类会话的 Gemini 响应白名单，位于全局 schema，不按机器人账号区分；没有记录的类别为空白名单。</summary>
[SugarTable("gemini_scope")]
public sealed class GeminiScopeRecord
{
    /// <summary>会话类别。</summary>
    [SugarColumn(ColumnName = "kind", IsPrimaryKey = true)]
    public GeminiScopeKind Kind { get; set; }

    /// <summary>允许响应的群号或好友 QQ 号，升序且不重复。</summary>
    [SugarColumn(ColumnName = "ids", ColumnDataType = "bigint[]", IsArray = true)]
    public long[] Ids { get; set; } = [];
}
