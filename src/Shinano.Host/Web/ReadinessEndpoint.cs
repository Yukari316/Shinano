using Shinano.Host.Restart;
using Shinano.Infrastructure.Health;

namespace Shinano.Host.Web;

/// <summary>就绪接口：报告启动阶段、各健康项等级与加载失败的插件 ID，不含详情文字。</summary>
internal static class ReadinessEndpoint
{
    /// <summary>当前就绪状态。</summary>
    internal static IResult Handle(RestartCoordinator restart, RuntimeHealth health) =>
        Results.Ok(
            new
            {
                phase              = restart.Phase,
                overall            = health.Overall,
                components         = health.Snapshot().Select(status => new { component = status.Component, level = status.Level }),
                pluginLoadFailures = restart.LoadFailures
            });
}
