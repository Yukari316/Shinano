namespace Shinano.Infrastructure.Health.Models;

/// <summary>一次健康等级变化，用于故障开始与恢复时各通知一次。</summary>
public sealed class HealthChange
{
    /// <summary>变化前的状态。</summary>
    public required HealthStatus Previous { get; init; }

    /// <summary>变化后的状态。</summary>
    public required HealthStatus Current { get; init; }
}
