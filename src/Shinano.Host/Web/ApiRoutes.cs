using Microsoft.AspNetCore.Mvc;
using Shinano.Host.Panel;
using Shinano.Host.Panel.Appearance;
using Shinano.Host.Panel.Auth;
using Shinano.Host.Panel.HostConfig;
using Shinano.Host.Panel.Stats;

namespace Shinano.Host.Web;

/// <summary>
/// 宿主全部 HTTP 路由的集中登记：路径、方法、鉴权与端点元数据只在此定义，处理方法位于各功能的 <c>Endpoints</c>。
/// 除登录外的 <c>/api</c> 接口要求面板会话；就绪接口免登录但只响应本机；其余路径返回管理面板单页应用。
/// </summary>
public static class ApiRoutes
{
    /// <summary>
    /// 就绪接口路径。这是跨进程契约：重启辅助程序按宿主写入的地址探测，监控进程 <c>Shinano.Monitor</c> 独立写有同一路径，
    /// 改动时须同步修改监控进程。
    /// </summary>
    public const string READINESS_PATH = "/healthz";

    /// <summary>插件专有页面资源的路径前缀，其后为 <c>/{插件 ID}/{web 下的相对路径}</c>。</summary>
    public const string PLUGIN_WEB_PREFIX = "/plugin-web";

    /// <summary>插件专有 API 的路径前缀，其后为 <c>/{插件 ID}</c>，插件在其下自行登记路由。</summary>
    public const string PLUGIN_API_PREFIX = "/api/plugin-api";

    /// <summary>登记全部路由。在认证与授权中间件之后调用。</summary>
    public static void MapApiRoutes(this WebApplication app)
    {
        app.MapGet(READINESS_PATH, ReadinessEndpoint.Handle)
           .AllowAnonymous()
           .AddEndpointFilter<LocalRequestFilter>();

        RouteGroupBuilder auth = app.MapGroup("/api/auth");
        auth.MapPost("/login", AuthEndpoints.LoginAsync);
        // 只接收 HttpContext 的方法会被当作 RequestDelegate 绑定并丢弃返回值，转为 Delegate 才会写出返回的结果。
        auth.MapPost("/logout", (Delegate)AuthEndpoints.LogoutAsync);
        auth.MapGet("/session", AuthEndpoints.Session).RequireAuthorization();
        auth.MapPost("/token", AuthEndpoints.Regenerate).RequireAuthorization();

        RouteGroupBuilder api = app.MapGroup("/api").RequireAuthorization();
        api.MapGet("/overview", StatusEndpoints.OverviewAsync);
        api.MapGet("/bot", StatusEndpoints.BotAsync);

        RouteGroupBuilder plugins = api.MapGroup("/plugins");
        plugins.MapGet("/", PluginEndpoints.ListAsync);
        plugins.MapPost("/{id}/enable", PluginEndpoints.EnableAsync);
        plugins.MapPost("/{id}/disable", PluginEndpoints.DisableAsync);
        plugins.MapPut("/order", PluginEndpoints.OrderAsync);
        plugins.MapGet("/{id}/config", PluginEndpoints.GetConfigAsync);
        plugins.MapPut("/{id}/config", PluginEndpoints.SaveConfigAsync);
        plugins.MapGet("/{id}/secrets", PluginEndpoints.GetSecretsAsync);
        plugins.MapPut("/{id}/secrets", PluginEndpoints.SaveSecretsAsync);
        app.MapGet($"{PLUGIN_WEB_PREFIX}/{{id}}/{{**path}}", PluginEndpoints.ServeWebFile).RequireAuthorization();

        RouteGroupBuilder stats = api.MapGroup("/stats");
        stats.MapGet("/daily", StatsEndpoints.DailyAsync);
        stats.MapGet("/conversations", StatsEndpoints.ConversationsAsync);
        stats.MapGet("/senders", StatsEndpoints.SendersAsync);
        api.MapGet("/archive-gaps", StatsEndpoints.GapsAsync);

        RouteGroupBuilder logs = api.MapGroup("/logs");
        logs.MapGet("/stream", LogEndpoints.StreamAsync);
        logs.MapGet("/files", LogEndpoints.ListFiles);
        logs.MapGet("/files/{name}", LogEndpoints.ReadFileAsync);

        api.MapGet("/config", ConfigEndpoints.Read);
        api.MapPut("/config", ConfigEndpoints.SaveAsync);

        // 会话 Cookie 为 SameSite=Strict，跨站请求不会携带会话，上传接口因此不另设防伪令牌。
        RouteGroupBuilder appearance = api.MapGroup("/appearance");
        appearance.MapGet("/", AppearanceEndpoints.Get);
        appearance.MapPut("/", AppearanceEndpoints.Update);
        appearance.MapGet("/background", AppearanceEndpoints.ServeBackground);
        appearance.MapPost("/background", AppearanceEndpoints.UploadAsync).DisableAntiforgery();
        appearance.MapDelete("/background", AppearanceEndpoints.DeleteBackground);

        RouteGroupBuilder packages = api.MapGroup("/packages");
        packages.MapGet("/", RestartEndpoints.Pending);
        packages.MapPost("/", RestartEndpoints.UploadAsync)
                .DisableAntiforgery()
                .WithMetadata(new RequestSizeLimitAttribute(RestartEndpoints.UPLOAD_REQUEST_LIMIT));
        packages.MapDelete("/{id}", RestartEndpoints.Undo);
        packages.MapPost("/{id}/remove", RestartEndpoints.StageRemoval);
        packages.MapGet("/{id}/data", RestartEndpoints.DataPreview);

        RouteGroupBuilder purges = api.MapGroup("/plugin-purges");
        purges.MapGet("/", RestartEndpoints.Purges);
        purges.MapDelete("/{id}", RestartEndpoints.DismissPurge);

        RouteGroupBuilder restart = api.MapGroup("/restart");
        restart.MapGet("/", RestartEndpoints.Status);
        restart.MapPost("/", RestartEndpoints.Restart);
        restart.MapDelete("/result", RestartEndpoints.DismissResult);

        // 插件专有 API 由插件在 PLUGIN_API_PREFIX/{插件 ID} 下自行登记，随插件启停出现与移除。
        ((IEndpointRouteBuilder)app).DataSources.Add(app.Services.GetRequiredService<PluginEndpointDataSource>());
        // 未匹配的 API 路径返回 404，而不是单页应用。
        app.Map("/api/{**rest}", () => Results.NotFound());
        // 单页应用的各路由都返回入口 HTML，同样要求浏览器每次验证缓存。
        app.MapFallbackToFile("index.html", PanelStaticFiles.Options);
    }
}
