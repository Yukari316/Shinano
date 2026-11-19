using Shinano.Host.Plugins.Staging;

namespace Shinano.Host.Restart;

/// <summary>最近一次带插件包变更的重启结果，保存在 <c>restart/result.json</c>，管理员在面板确认后删除。</summary>
public sealed class RestartResult
{
    /// <summary>结果。</summary>
    public required RestartOutcome Outcome { get; init; }

    /// <summary>回滚或未验证的原因；成功时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>完成时间（UTC）。</summary>
    public required DateTimeOffset FinishedAt { get; init; }

    /// <summary>本次重启涉及的变更。</summary>
    public required IReadOnlyList<PluginChange> Changes { get; init; }
}

/// <summary>一次带插件包变更的重启的结果。</summary>
public enum RestartOutcome
{
    /// <summary>变更的插件包全部加载成功。</summary>
    Succeeded,

    /// <summary>新版本未能就绪，已还原上一套插件包。</summary>
    RolledBack,

    /// <summary>基础设施处于故障模式，插件包已应用但未经加载验证。</summary>
    Unverified
}
