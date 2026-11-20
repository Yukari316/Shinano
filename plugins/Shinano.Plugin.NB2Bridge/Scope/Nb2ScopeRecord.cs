using SqlSugar;

namespace Shinano.Plugin.NB2Bridge.Scope;

/// <summary>一类会话的 NB2 响应范围，位于全局 schema，不按机器人账号区分；没有记录的类别为空黑名单。</summary>
[SugarTable("nb2_bridge_scope")]
public sealed class Nb2ScopeRecord
{
    /// <summary>会话类别。</summary>
    [SugarColumn(ColumnName = "kind", IsPrimaryKey = true)]
    public Nb2ScopeKind Kind { get; set; }

    /// <summary>为 true 时只交付名单内的会话，为 false 时交付名单外的会话。</summary>
    [SugarColumn(ColumnName = "whitelist")]
    public bool Whitelist { get; set; }

    /// <summary>群号或好友 QQ 号，升序且不重复。</summary>
    [SugarColumn(ColumnName = "ids", ColumnDataType = "bigint[]", IsArray = true)]
    public long[] Ids { get; set; } = [];
}
