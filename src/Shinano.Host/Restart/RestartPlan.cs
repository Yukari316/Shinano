using Shinano.Host.Plugins.Purge;
using Shinano.Host.Plugins.Staging;

namespace Shinano.Host.Restart;

/// <summary>
/// 面板重启时写入的插件包变更计划，保存在 <c>restart/plan.json</c>。新进程在加载插件前应用，
/// 加载验证通过后删除；验证前的任何一次启动都会按其中记录的备份还原。
/// </summary>
public sealed class RestartPlan
{
    /// <summary>发起时间（UTC）。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>进度。</summary>
    public RestartPlanState State { get; set; } = RestartPlanState.Pending;

    /// <summary>加载失败的原因；只在 <see cref="RestartPlanState.LoadFailed" /> 时有值。</summary>
    public string? FailureReason { get; set; }

    /// <summary>按顺序执行的操作。</summary>
    public required List<PluginPackageOperation> Operations { get; init; }
}

/// <summary>重启计划的进度；验证通过后计划即删除，因此除 <see cref="Pending" /> 外的任何状态在启动时都会还原备份。</summary>
public enum RestartPlanState
{
    /// <summary>已由重启操作写入，等待新进程启动时应用。</summary>
    Pending,

    /// <summary>正在应用；进程在此期间退出时下一次启动还原。</summary>
    Applying,

    /// <summary>已应用，等待新进程验证插件包加载。</summary>
    Applied,

    /// <summary>变更的插件包加载失败，下一次启动还原。</summary>
    LoadFailed
}

/// <summary>重启计划中对一个插件目录的操作。</summary>
public sealed class PluginPackageOperation
{
    /// <summary>变更内容。</summary>
    public required PluginChange Change { get; init; }

    /// <summary>暂存的插件包目录；删除时为 null。</summary>
    public string? StagedDirectory { get; init; }

    /// <summary>
    /// 插件目录下的目标目录：升级与删除为已安装的目录，安装为 <c>&lt;插件目录&gt;/&lt;id&gt;</c>，建立计划时该目录不存在。
    /// </summary>
    public required string TargetDirectory { get; init; }

    /// <summary>删除并清除数据时的清除范围，建立计划时取自已安装插件的清单；其他情况为 null。</summary>
    public PluginDataScope? Purge { get; init; }
}
