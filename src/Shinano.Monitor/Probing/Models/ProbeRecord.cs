namespace Shinano.Monitor.Probing.Models;

/// <summary>一次探测的结果，每条在 <c>data/probes-&lt;日期&gt;.jsonl</c> 中占一行。</summary>
public sealed class ProbeRecord
{
    /// <summary>探测开始时间（UTC）。</summary>
    public required DateTimeOffset Time { get; init; }

    /// <summary>服务。</summary>
    public required MonitoredService Service { get; init; }

    /// <summary>等级。</summary>
    public required HealthLevel Level { get; init; }

    /// <summary>耗时（毫秒）。</summary>
    public required int LatencyMs { get; init; }

    /// <summary>失败类别；正常时为 null。</summary>
    public ProbeFailure? Failure { get; init; }

    /// <summary><see cref="ProbeFailure.HttpStatus" /> 时的状态码。</summary>
    public int? StatusCode { get; init; }
}
