namespace Shinano.Monitor;

/// <summary>
/// 按间隔并行探测全部服务并写入记录；某项等级变化时写一条日志（告警只体现在状态页与日志，不发 IM）。
/// 每小时清理一次过期记录与超出大小上限的日志。
/// </summary>
public sealed class ProbeService : BackgroundService
{
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);

    private readonly ServiceProber                          _prober;
    private readonly ProbeStore                             _store;
    private readonly MonitorSettings                        _settings;
    private readonly LogRetention                           _logRetention;
    private readonly TimeProvider                           _time;
    private readonly ILogger<ProbeService>                  _logger;
    private readonly Dictionary<MonitoredService, HealthLevel> _levels = [];

    /// <summary>创建服务。</summary>
    public ProbeService(
        ServiceProber         prober,
        ProbeStore            store,
        MonitorSettings       settings,
        LogRetention          logRetention,
        TimeProvider          time,
        ILogger<ProbeService> logger)
    {
        _prober       = prober;
        _store        = store;
        _settings     = settings;
        _logRetention = logRetention;
        _time         = time;
        _logger       = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset lastMaintenance = _time.GetUtcNow();
        foreach (ProbeRecord record in _store.Snapshot().GroupBy(record => record.Service).Select(group => group.Last()))
            _levels[record.Service] = record.Level;

        using PeriodicTimer timer = new(_settings.Interval, _time);
        do
        {
            ProbeRecord[] round = await Task.WhenAll(
                Enum.GetValues<MonitoredService>().Select(service => _prober.ProbeAsync(service, stoppingToken)));
            DateTimeOffset now = _time.GetUtcNow();
            try
            {
                _store.Append(round, now);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(exception, "无法写入探测记录");
            }

            foreach (ProbeRecord record in round) Track(record);
            if (now - lastMaintenance >= MaintenanceInterval)
            {
                _store.Prune(now);
                _logRetention.Clean();
                lastMaintenance = now;
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private void Track(ProbeRecord record)
    {
        bool known = _levels.TryGetValue(record.Service, out HealthLevel previous);
        _levels[record.Service] = record.Level;
        if (known && previous == record.Level) return;

        if (record.Level == HealthLevel.Normal)
            _logger.LogInformation("{Service} 状态：{Level}", record.Service, record.Level);
        else
            _logger.LogWarning(
                "{Service} 状态：{Level}（{Failure}{StatusCode}）",
                record.Service,
                record.Level,
                record.Failure,
                record.StatusCode is { } code ? $" {code}" : string.Empty);
    }
}
