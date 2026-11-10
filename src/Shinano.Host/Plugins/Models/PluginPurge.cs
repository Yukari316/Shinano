namespace Shinano.Host.Plugins.Models;

/// <summary>
/// 一个插件的数据清除任务与结果，保存在部署数据目录的 <c>plugin-purge/&lt;id&gt;.json</c>。
/// 由删除插件包并选择清除数据的重启确认成功后建立，完成且管理员关闭显示后删除。
/// </summary>
public sealed class PluginPurge
{
    /// <summary>插件 ID。</summary>
    public required string PluginId { get; init; }

    /// <summary>显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>清除范围，取自删除时的插件清单。</summary>
    public required PluginDataScope Scope { get; init; }

    /// <summary>建立时间（UTC）。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>进度。</summary>
    public PluginPurgeState State { get; set; } = PluginPurgeState.Waiting;

    /// <summary>最近一次执行结束的时间（UTC）。</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    /// <summary>各步骤的结果，按执行顺序。</summary>
    public required List<PluginPurgeStep> Steps { get; init; }

    /// <summary>名字以插件前缀开头、但未声明的表（<c>schema.表名</c>），只报告不删除。</summary>
    public List<string> PossibleLeftovers { get; set; } = [];

    /// <summary>管理员是否已关闭显示；重试后重新显示。</summary>
    public bool Dismissed { get; set; }
}
