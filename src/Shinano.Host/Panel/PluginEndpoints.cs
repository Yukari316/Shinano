using System.Text.Json.Nodes;
using Microsoft.AspNetCore.StaticFiles;
using Shinano.Host.Plugins;

namespace Shinano.Host.Panel;

/// <summary>插件管理：状态、启停、顺序、配置表单与专有面板的页面资源。</summary>
internal static class PluginEndpoints
{
    /// <summary>专有面板页面资源的路径前缀。</summary>
    public const string WEB_PREFIX = "/plugin-web";

    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>登记端点。</summary>
    public static void Map(RouteGroupBuilder api, IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = api.MapGroup("/plugins");
        group.MapGet("/", (PluginRuntime plugins, CancellationToken cancellationToken) => plugins.SnapshotAsync(cancellationToken));
        group.MapPost("/{id}/enable", EnableAsync);
        group.MapPost("/{id}/disable", DisableAsync);
        group.MapPut("/order", OrderAsync);
        group.MapGet("/{id}/config", GetConfigAsync);
        group.MapPut("/{id}/config", SaveConfigAsync);
        app.MapGet($"{WEB_PREFIX}/{{id}}/{{**path}}", ServeWebFile).RequireAuthorization();
    }

    private static async Task<IResult> EnableAsync(string id, PluginRuntime plugins, CancellationToken cancellationToken) =>
        await plugins.EnableAsync(id, cancellationToken) is { } error
            ? PanelResults.Error(StatusCodes.Status409Conflict, error)
            : Results.NoContent();

    private static async Task<IResult> DisableAsync(string id, PluginRuntime plugins, CancellationToken cancellationToken) =>
        await plugins.DisableAsync(id, cancellationToken) is { } error
            ? PanelResults.Error(StatusCodes.Status409Conflict, error)
            : Results.NoContent();

    private static async Task<IResult> OrderAsync(string[] ids, PluginRuntime plugins, CancellationToken cancellationToken)
    {
        await plugins.SetOrderAsync(ids, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetConfigAsync(string id, PluginRuntime plugins, CancellationToken cancellationToken) =>
        await plugins.GetConfigAsync(id, cancellationToken) is { } view
            ? Results.Ok(view)
            : PanelResults.Error(StatusCodes.Status404NotFound, "插件不存在或没有配置表单");

    private static async Task<IResult> SaveConfigAsync(
        string            id,
        JsonNode?         config,
        PluginRuntime     plugins,
        CancellationToken cancellationToken) =>
        await plugins.SaveConfigAsync(id, config, cancellationToken) switch
        {
            null                     => PanelResults.Error(StatusCodes.Status404NotFound, "插件不存在或没有配置表单"),
            { Count: 0 }             => Results.NoContent(),
            IReadOnlyList<string> errors => Results.Json(new { message = "配置无效", errors }, statusCode: StatusCodes.Status400BadRequest)
        };

    // 只在插件启用且登记了页面时提供 web/ 下的文件，路径不能越出 web/。
    private static IResult ServeWebFile(string id, string path, PluginRuntime plugins)
    {
        if (plugins.FindActivePage(id)?.ResolveWebFile(path) is not { } file) return Results.NotFound();

        string contentType = ContentTypes.TryGetContentType(file, out string? type) ? type : "application/octet-stream";
        return Results.File(file, contentType);
    }
}
