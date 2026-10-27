using Microsoft.Extensions.Logging;
using Shinano.Infrastructure.Health.Models;

namespace Shinano.Infrastructure.Health;

/// <summary>
/// 运行中外部依赖（数据库、RustFS）的可用性：功能被触发时按最小间隔节流探测，同时发现故障与恢复；
/// 调用方遇到连接故障时也可直接报告；健康检查指令强制探测。
/// </summary>
public sealed class DependencyAvailability
{
    private readonly HealthComponent               _component;
    private readonly HealthLevel                   _failureLevel;
    private readonly Func<CancellationToken, Task> _probe;
    private readonly TimeSpan                      _minProbeInterval;
    private readonly RuntimeHealth                 _health;
    private readonly TimeProvider                  _time;
    private readonly ILogger                       _logger;
    private readonly SemaphoreSlim                 _probeGate = new(1, 1);
    private          long                          _lastProbeTicks;

    /// <summary>创建可用性跟踪。</summary>
    /// <param name="component">对应的健康组成部分。</param>
    /// <param name="failureLevel">不可用时的健康等级。</param>
    /// <param name="probe">检查依赖是否可用，不可用时抛出异常。</param>
    /// <param name="minProbeInterval">非强制检查的最小间隔。</param>
    /// <param name="health">运行健康状态。</param>
    /// <param name="time">时间来源。</param>
    /// <param name="logger">日志。</param>
    public DependencyAvailability(
        HealthComponent               component,
        HealthLevel                   failureLevel,
        Func<CancellationToken, Task> probe,
        TimeSpan                      minProbeInterval,
        RuntimeHealth                 health,
        TimeProvider                  time,
        ILogger                       logger)
    {
        _component        = component;
        _failureLevel     = failureLevel;
        _probe            = probe;
        _minProbeInterval = minProbeInterval;
        _health           = health;
        _time             = time;
        _logger           = logger;
    }

    /// <summary>依赖当前是否可用。</summary>
    public bool IsAvailable => _health.Get(_component).Level == HealthLevel.Normal;

    /// <summary>报告一次连接故障，使依赖进入不可用状态。</summary>
    public void ReportFailure(Exception exception)
    {
        if (IsAvailable) _logger.LogError(exception, "{Component} 不可用", _component);
        MarkProbed();
        _health.Set(_component, _failureLevel, exception.Message);
    }

    /// <summary>
    /// 探测依赖并更新健康状态。非强制探测距上次探测不足最小间隔时直接返回当前状态。
    /// </summary>
    /// <returns>依赖是否可用。</returns>
    public async Task<bool> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        if (!force && _time.GetUtcNow().UtcTicks - Volatile.Read(ref _lastProbeTicks) < _minProbeInterval.Ticks)
            return IsAvailable;

        await _probeGate.WaitAsync(cancellationToken);
        try
        {
            if (!force && _time.GetUtcNow().UtcTicks - Volatile.Read(ref _lastProbeTicks) < _minProbeInterval.Ticks)
                return IsAvailable;

            MarkProbed();
            await _probe(cancellationToken);
            if (!IsAvailable) _logger.LogInformation("{Component} 已恢复", _component);
            _health.Set(_component, HealthLevel.Normal, string.Empty);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (IsAvailable) _logger.LogError(exception, "{Component} 不可用", _component);
            _health.Set(_component, _failureLevel, exception.Message);
            return false;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    private void MarkProbed() => Volatile.Write(ref _lastProbeTicks, _time.GetUtcNow().UtcTicks);
}
