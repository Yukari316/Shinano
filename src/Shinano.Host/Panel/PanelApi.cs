namespace Shinano.Host.Panel;

/// <summary>管理面板的路由：除登录外的 API 都要求面板会话；其余路径返回单页应用。</summary>
public static class PanelApi
{
    /// <summary>登记面板 API、插件管理 API 与单页应用回退。</summary>
    public static void MapPanel(this WebApplication app)
    {
        AuthEndpoints.Map(app);
        RouteGroupBuilder api = app.MapGroup("/api").RequireAuthorization();
        StatusEndpoints.Map(api);
        PluginEndpoints.Map(api, app);
        StatsEndpoints.Map(api);
        LogEndpoints.Map(api);
        ConfigEndpoints.Map(api);
        AppearanceEndpoints.Map(api);
        ((IEndpointRouteBuilder)app).DataSources.Add(app.Services.GetRequiredService<PluginEndpointDataSource>());
        // 未匹配的 API 路径返回 404，而不是单页应用。
        app.Map("/api/{**rest}", () => Results.NotFound());
        app.MapFallbackToFile("index.html");
    }
}
