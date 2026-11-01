using Shinano.Infrastructure.Database;
using SqlSugar;

namespace Shinano.Host.Plugins.Entities;

/// <summary>插件的普通配置（不含机密），位于全局 schema，切换机器人账号后保留。</summary>
[SugarTable("plugin_config")]
public sealed class PluginConfigRecord
{
    /// <summary>插件 ID。</summary>
    [SugarColumn(ColumnName = "plugin_id", IsPrimaryKey = true, Length = 64)]
    public string PluginId { get; set; } = string.Empty;

    /// <summary>配置 JSON。</summary>
    [SugarColumn(ColumnName = "config", ColumnDataType = "jsonb", SqlParameterDbType = typeof(JsonbStringConverter))]
    public string Config { get; set; } = "{}";

    /// <summary>最近修改时间。</summary>
    [SugarColumn(ColumnName = "updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}
