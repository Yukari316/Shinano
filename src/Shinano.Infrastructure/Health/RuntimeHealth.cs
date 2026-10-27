using Shinano.Infrastructure.Health.Models;

namespace Shinano.Infrastructure.Health;

/// <summary>
/// 宿主唯一维护的运行健康状态，面板、健康检查指令与健康接口都从这里读取。
/// 整体等级取各组成部分中最严重的一级。
/// </summary>
public sealed class RuntimeHealth
{
    private readonly Lock                                         _lock     = new();
    private readonly Dictionary<HealthComponent, HealthStatus> _statuses = new();
    private readonly TimeProvider                                 _time;

    /// <summary>创建状态表，全部组成部分初始为正常。</summary>
    public RuntimeHealth(TimeProvider time)
    {
        _time = time;
        DateTimeOffset now = time.GetUtcNow();
        foreach (HealthComponent component in Enum.GetValues<HealthComponent>())
            _statuses[component] = new HealthStatus
            {
                Component = component,
                Level     = HealthLevel.Normal,
                Detail    = string.Empty,
                Since     = now
            };
    }

    /// <summary>
    /// 等级发生变化时触发，在调用 <see cref="Set" /> 的线程上同步执行；只改变原因不触发。
    /// 处理器异常会传播给调用方，处理器应自行捕获。
    /// </summary>
    public event Action<HealthChange>? Changed;

    /// <summary>整体等级。</summary>
    public HealthLevel Overall
    {
        get
        {
            lock (_lock)
            {
                return _statuses.Values.Max(status => status.Level);
            }
        }
    }

    /// <summary>返回全部组成部分的当前状态。</summary>
    public IReadOnlyList<HealthStatus> Snapshot()
    {
        lock (_lock)
        {
            return [.. _statuses.Values.OrderBy(status => status.Component)];
        }
    }

    /// <summary>返回某个组成部分的当前状态。</summary>
    public HealthStatus Get(HealthComponent component)
    {
        lock (_lock)
        {
            return _statuses[component];
        }
    }

    /// <summary>设置某个组成部分的等级与原因。</summary>
    /// <param name="component">组成部分。</param>
    /// <param name="level">新等级。</param>
    /// <param name="detail">原因；恢复正常时传空字符串。</param>
    public void Set(HealthComponent component, HealthLevel level, string detail)
    {
        HealthChange? change = null;
        lock (_lock)
        {
            HealthStatus previous = _statuses[component];
            if (previous.Level == level && previous.Detail == detail) return;

            HealthStatus current = new()
            {
                Component = component,
                Level     = level,
                Detail    = detail,
                Since     = previous.Level == level ? previous.Since : _time.GetUtcNow()
            };
            _statuses[component] = current;
            if (previous.Level != level)
                change = new HealthChange
                {
                    Previous = previous,
                    Current  = current
                };
        }

        if (change is not null) Changed?.Invoke(change);
    }
}
