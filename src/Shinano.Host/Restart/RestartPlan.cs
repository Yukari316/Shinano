namespace Shinano.Host.Restart;

/// <summary>
/// 面板重启时写入的插件包变更计划，保存在 <c>restart/plan.json</c>。新进程在加载插件前应用，
/// 加载验证通过后删除；验证前的任何一次启动都会按其中记录的备份还原。
/// </summary>
public sealed class RestartPlan
{
    /// <summary>发起重启时的重启方式。</summary>
    public required RestartMode Mode { get; init; }

    /// <summary>发起时间（UTC）。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>进度。</summary>
    public RestartPlanState State { get; set; } = RestartPlanState.Pending;

    /// <summary>加载失败的原因；只在 <see cref="RestartPlanState.LoadFailed" /> 时有值。</summary>
    public string? FailureReason { get; set; }

    /// <summary>按顺序执行的操作。</summary>
    public required List<PluginPackageOperation> Operations { get; init; }
}
