using Shinano.Monitor.Probing.Models;

namespace Shinano.Monitor.Status.Models;

/// <summary>状态条带的一格。</summary>
public sealed class StatusBar
{
    /// <summary>开始时间（UTC）。</summary>
    public required DateTimeOffset Start { get; init; }

    /// <summary>这一格内最差的等级；没有记录时为 null。</summary>
    public HealthLevel? Level { get; init; }

    /// <summary>探测次数。</summary>
    public required int Total { get; init; }

    /// <summary>其中停止的次数。</summary>
    public required int Stopped { get; init; }
}
