namespace Shinano.Infrastructure.Health;

/// <summary>某个组成部分的当前健康状态。</summary>
public sealed class HealthStatus
{
    /// <summary>组成部分。</summary>
    public required HealthComponent Component { get; init; }

    /// <summary>健康等级。</summary>
    public required HealthLevel Level { get; init; }

    /// <summary>不正常时的原因；正常时为空字符串。</summary>
    public required string Detail { get; init; }

    /// <summary>进入当前等级的时间。</summary>
    public required DateTimeOffset Since { get; init; }
}
