using Shinano.Monitor.Probing.Models;

namespace Shinano.Monitor.Status.Models;

/// <summary>状态页中一项服务的数据。</summary>
public sealed class ServiceStatus
{
    /// <summary>服务。</summary>
    public required MonitoredService Service { get; init; }

    /// <summary>最近一次探测的等级；还没有探测时为 null。</summary>
    public HealthLevel? Level { get; init; }

    /// <summary>最近一次探测时间（UTC）。</summary>
    public DateTimeOffset? CheckedAt { get; init; }

    /// <summary>最近一次探测耗时（毫秒）。</summary>
    public int? LatencyMs { get; init; }

    /// <summary>最近一次探测的失败类别。</summary>
    public ProbeFailure? Failure { get; init; }

    /// <summary>失败类别为 HTTP 状态码时的状态码。</summary>
    public int? StatusCode { get; init; }

    /// <summary>最近 24 小时可用率（0–1）；没有记录时为 null。</summary>
    public double? Availability24h { get; init; }

    /// <summary>最近 7 天可用率（0–1）；没有记录时为 null。</summary>
    public double? Availability7d { get; init; }

    /// <summary>最近 24 小时条带，从早到晚。</summary>
    public required IReadOnlyList<StatusBar> Bars { get; init; }
}
