using Shinano.Monitor.Configuration;
using Shinano.Monitor.Probing;

namespace Shinano.Monitor.Status.Endpoints;

/// <summary>状态页数据接口。</summary>
internal static class StatusEndpoints
{
    /// <summary>按保留期内的探测记录计算当前状态、可用率与条带。</summary>
    internal static IResult Get(ProbeStore probes, TimeProvider time, MonitorSettings settings) =>
        Results.Ok(StatusReport.Create(probes.Snapshot(), time.GetUtcNow(), settings));
}
