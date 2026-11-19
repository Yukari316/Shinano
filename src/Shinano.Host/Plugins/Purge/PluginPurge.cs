namespace Shinano.Host.Plugins.Purge;

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

/// <summary>一次插件数据清除的进度。</summary>
public enum PluginPurgeState
{
    /// <summary>等待数据库与 RustFS 可用后执行。</summary>
    Waiting,

    /// <summary>正在执行。</summary>
    Running,

    /// <summary>全部步骤完成。</summary>
    Completed,

    /// <summary>有步骤失败，下次启动重试失败的步骤。</summary>
    PartiallyFailed,

    /// <summary>执行前发现同 ID 的插件已重新安装，放弃剩余步骤，避免删除新插件的数据。</summary>
    Cancelled
}

/// <summary>插件数据清除中一个步骤的结果。</summary>
public sealed class PluginPurgeStep
{
    /// <summary>步骤。</summary>
    public required PluginPurgeStepKind Kind { get; init; }

    /// <summary>是否已完成；未完成的步骤在下次执行时重试。</summary>
    public bool Done { get; set; }

    /// <summary>完成时的结果说明（删除数量等），面向管理员显示。</summary>
    public string? Summary { get; set; }

    /// <summary>最近一次失败的原因；成功后清除。</summary>
    public string? Error { get; set; }
}

/// <summary>插件数据清除的步骤，按声明顺序执行。</summary>
public enum PluginPurgeStepKind
{
    /// <summary>删除声明的数据表。</summary>
    Tables,

    /// <summary>删除各 schema 中该插件的结构版本记录。</summary>
    SchemaVersions,

    /// <summary>删除持久媒体引用。</summary>
    MediaReferences,

    /// <summary>清空并删除插件 bucket。</summary>
    Buckets,

    /// <summary>删除配置、启用状态与顺序。</summary>
    ConfigAndState,

    /// <summary>删除机密文件。</summary>
    Secrets,

    /// <summary>删除浏览器用户目录。</summary>
    BrowserProfile,

    /// <summary>查找名字以插件前缀开头、但未声明的表，只报告不删除。</summary>
    Leftovers
}
