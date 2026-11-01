using Shinano.Monitor.Probing.Models;
using Shinano.Monitor.Status.Models;
using Shinano.Monitor.Web.Models;

namespace Shinano.Monitor.Status;

/// <summary>状态页数据：整体状态与每项服务的当前状态、可用率和最近 24 小时条带。</summary>
public sealed class StatusReport
{
    /// <summary>条带格数。</summary>
    public const int BAR_COUNT = 48;

    /// <summary>每格时长。</summary>
    public static readonly TimeSpan BarLength = TimeSpan.FromMinutes(30);

    private static readonly TimeSpan Day  = TimeSpan.FromDays(1);
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>整体等级；还没有任何探测结果时为 null。</summary>
    public HealthLevel? Overall { get; init; }

    /// <summary>生成时间（UTC）。</summary>
    public required DateTimeOffset GeneratedAt { get; init; }

    /// <summary>探测间隔（秒）。</summary>
    public required int IntervalSeconds { get; init; }

    /// <summary>是否找到了部署配置文件。</summary>
    public required bool Configured { get; init; }

    /// <summary>部署时区的 IANA 或 Windows ID，页面据此显示时间。</summary>
    public required string TimeZone { get; init; }

    /// <summary>各服务，按 <see cref="MonitoredService" /> 顺序。</summary>
    public required IReadOnlyList<ServiceStatus> Services { get; init; }

    /// <summary>
    /// 计算状态页数据。可用率以正常与降级为可用、停止为不可用；条带按半小时对齐，最后一格是当前半小时，
    /// 每格取其中最差的等级，没有记录的格为 null。
    /// </summary>
    public static StatusReport Create(IReadOnlyList<ProbeRecord> records, DateTimeOffset now, MonitorSettings settings)
    {
        long barTicks = BarLength.Ticks;
        DateTimeOffset lastBarStart = new(now.UtcTicks - now.UtcTicks % barTicks, TimeSpan.Zero);
        DateTimeOffset firstBarStart = lastBarStart - BarLength * (BAR_COUNT - 1);

        List<ServiceStatus> services = [];
        foreach (MonitoredService service in Enum.GetValues<MonitoredService>())
        {
            List<ProbeRecord> history = [.. records.Where(record => record.Service == service)];
            ProbeRecord? latest = history.Count > 0 ? history[^1] : null;
            List<StatusBar> bars = [];
            for (int index = 0; index < BAR_COUNT; index++)
            {
                DateTimeOffset start = firstBarStart + BarLength * index;
                DateTimeOffset end = start + BarLength;
                List<ProbeRecord> inBar = [.. history.Where(record => record.Time >= start && record.Time < end)];
                bars.Add(
                    new StatusBar
                    {
                        Start   = start,
                        Level   = inBar.Count > 0 ? inBar.Max(record => record.Level) : null,
                        Total   = inBar.Count,
                        Stopped = inBar.Count(record => record.Level == HealthLevel.Stopped)
                    });
            }

            services.Add(
                new ServiceStatus
                {
                    Service         = service,
                    Level           = latest?.Level,
                    CheckedAt       = latest?.Time,
                    LatencyMs       = latest?.LatencyMs,
                    Failure         = latest?.Failure,
                    StatusCode      = latest?.StatusCode,
                    Availability24h = Availability(history, now - Day),
                    Availability7d  = Availability(history, now - Week),
                    Bars            = bars
                });
        }

        return new StatusReport
        {
            Overall         = CombinedLevel(services),
            GeneratedAt     = now,
            IntervalSeconds = (int)settings.Interval.TotalSeconds,
            Configured      = settings.ConfigPath is not null,
            TimeZone        = settings.TimeZone.Id,
            Services        = services
        };
    }

    /// <summary>
    /// 整体等级按主进程的健康映射：RustFS 停止只算降级，其余服务停止算停止；取各项中最差的一级。
    /// </summary>
    public static HealthLevel? CombinedLevel(IReadOnlyList<ServiceStatus> services)
    {
        List<HealthLevel> levels =
        [
            .. services.Where(status => status.Level is not null)
                       .Select(status => status is { Service: MonitoredService.ObjectStorage, Level: HealthLevel.Stopped }
                           ? HealthLevel.Degraded
                           // SAFE: 上一行已筛除没有结果的服务。
                           : status.Level!.Value)
        ];
        return levels.Count == 0 ? null : levels.Max();
    }

    private static double? Availability(List<ProbeRecord> history, DateTimeOffset since)
    {
        List<ProbeRecord> window = [.. history.Where(record => record.Time >= since)];
        return window.Count == 0 ? null : (double)window.Count(record => record.Level != HealthLevel.Stopped) / window.Count;
    }
}
