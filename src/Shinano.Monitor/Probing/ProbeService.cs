using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Shinano.Monitor.Web;

namespace Shinano.Monitor.Probing;

/// <summary>
/// 按间隔并行探测全部服务并写入记录；某项等级变化时写一条日志（告警只体现在状态页与日志，不发 IM）。
/// 每小时清理一次过期记录与超出大小上限的日志。部署配置文件变化后，下一轮探测前按新配置重建探测器。
/// </summary>
public sealed class ProbeService : BackgroundService
{
    private static readonly TimeSpan MaintenanceInterval = TimeSpan.FromHours(1);

    private readonly ProbeStore                             _store;
    private readonly MonitorSettings                        _settings;
    private readonly LogRetention                           _logRetention;
    private readonly TimeProvider                           _time;
    private readonly ILogger<ProbeService>                  _logger;
    private readonly Dictionary<MonitoredService, HealthLevel> _levels = [];
    private          ServiceProber                          _prober;
    private volatile bool                                   _configChanged;

    /// <summary>创建服务。</summary>
    public ProbeService(
        ProbeStore            store,
        MonitorSettings       settings,
        LogRetention          logRetention,
        TimeProvider          time,
        ILogger<ProbeService> logger)
    {
        _prober       = new ServiceProber(settings, time);
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

        // 主进程面板可改监听地址、端口与证书等探测目标；宿主以临时文件替换写入，监视文件名即可收到变化。
        string? configPath = _settings.ConfigPath;
        using PhysicalFileProvider? files = configPath is null ? null : new PhysicalFileProvider(Path.GetDirectoryName(configPath) ?? ".");
        using IDisposable? watch = files is null || configPath is null
            ? null
            : ChangeToken.OnChange(() => files.Watch(Path.GetFileName(configPath)), () => _configChanged = true);

        using PeriodicTimer timer = new(_settings.Interval, _time);
        do
        {
            if (_configChanged) ReloadProber();
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

    /// <inheritdoc />
    public override void Dispose()
    {
        _prober.Dispose();
        base.Dispose();
    }

    // 只在探测循环中、两轮之间调用，旧探测器此时没有进行中的请求，可以直接释放。
    private void ReloadProber()
    {
        _configChanged = false;
        try
        {
            ServiceProber next = new(_settings.Reload(), _time);
            _prober.Dispose();
            _prober = next;
            _logger.LogInformation("部署配置已变化，按新配置探测");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            _logger.LogWarning(exception, "部署配置已变化但无法读取，继续使用原配置探测，直到文件再次变化");
        }
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
