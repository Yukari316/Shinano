using Shinano.Host.Packages;

namespace Shinano.Host.Restart;

/// <summary>最近一次带插件包变更的重启结果，保存在 <c>restart/result.json</c>，管理员在面板确认后删除。</summary>
public sealed class RestartResult
{
    /// <summary>结果。</summary>
    public required RestartOutcome Outcome { get; init; }

    /// <summary>重启方式。</summary>
    public required RestartMode Mode { get; init; }

    /// <summary>回滚或未验证的原因；成功时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>完成时间（UTC）。</summary>
    public required DateTimeOffset FinishedAt { get; init; }

    /// <summary>本次重启涉及的变更。</summary>
    public required IReadOnlyList<PluginChange> Changes { get; init; }
}
