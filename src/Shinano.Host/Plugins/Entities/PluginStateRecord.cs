using SqlSugar;

namespace Shinano.Host.Plugins.Entities;

/// <summary>插件的持久化启用状态与顺序，位于全局 schema，切换机器人账号后保留。</summary>
[SugarTable("plugin_state")]
public sealed class PluginStateRecord
{
    /// <summary>插件 ID。</summary>
    [SugarColumn(ColumnName = "plugin_id", IsPrimaryKey = true, Length = 64)]
    public string PluginId { get; set; } = string.Empty;

    /// <summary>是否启用。新安装的插件默认不启用。</summary>
    [SugarColumn(ColumnName = "enabled")]
    public bool Enabled { get; set; }

    /// <summary>插件顺序，数值小者先处理指令与普通事件。</summary>
    [SugarColumn(ColumnName = "sort_order")]
    public int SortOrder { get; set; }

    /// <summary>最近修改时间。</summary>
    [SugarColumn(ColumnName = "updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
