using Shinano.Host.Restart;
using Shinano.Infrastructure.Health;

namespace Shinano.Host.Web;

/// <summary>
/// 免登录的就绪接口，供本机的重启辅助程序与监控进程使用：报告启动阶段、各健康项等级与加载失败的插件 ID，
/// 不含详情文字。只响应来自本机的请求（回环地址，或面板监听的本机地址），其余返回 404。
/// </summary>
public static class ReadinessEndpoint
{
    /// <summary>接口路径。</summary>
    public const string PATH = "/healthz";

    /// <summary>登记接口。</summary>
    public static void MapReadiness(this IEndpointRouteBuilder app) =>
        app.MapGet(PATH, Handle);

    private static IResult Handle(HttpContext context, RestartCoordinator restart, RuntimeHealth health)
    {
        ConnectionInfo connection = context.Connection;
        bool local = connection.RemoteIpAddress is { } remote
                     && (System.Net.IPAddress.IsLoopback(remote) || remote.Equals(connection.LocalIpAddress));
        if (!local) return Results.NotFound();

        return Results.Ok(
            new
            {
                phase              = restart.Phase,
                overall            = health.Overall,
                components         = health.Snapshot().Select(status => new { component = status.Component, level = status.Level }),
                pluginLoadFailures = restart.LoadFailures
            });
    }
}
