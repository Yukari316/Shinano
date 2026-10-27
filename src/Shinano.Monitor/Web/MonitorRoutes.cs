using Shinano.Monitor.Status.Endpoints;

namespace Shinano.Monitor.Web;

/// <summary>
/// 监控进程全部 HTTP 路由的集中登记：路径与方法只在此定义，处理方法位于各功能的 <c>Endpoints</c>。
/// 页面只读、无需登录；其余路径返回状态页单页应用。
/// </summary>
public static class MonitorRoutes
{
    /// <summary>登记全部路由。在静态文件中间件之后调用。</summary>
    public static void MapMonitorRoutes(this WebApplication app)
    {
        app.MapGet("/api/status", StatusEndpoints.Get);
        // 未匹配的 API 路径返回 404，而不是单页应用。
        app.Map("/api/{**rest}", () => Results.NotFound());
        app.MapFallbackToFile("index.html");
    }
}
