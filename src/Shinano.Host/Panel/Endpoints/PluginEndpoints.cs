using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.StaticFiles;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;

namespace Shinano.Host.Panel.Endpoints;

/// <summary>插件管理：状态、启停、顺序、配置表单、机密表单与专有面板的页面资源。</summary>
internal static class PluginEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>全部插件的状态快照。</summary>
    internal static Task<IReadOnlyList<PluginSnapshot>> ListAsync(PluginRuntime plugins, CancellationToken cancellationToken) =>
        plugins.SnapshotAsync(cancellationToken);

    internal static async Task<IResult> EnableAsync(string id, PluginRuntime plugins, CancellationToken cancellationToken) =>
        await plugins.EnableAsync(id, cancellationToken) is { } error
            ? PanelResults.Error(StatusCodes.Status409Conflict, error)
            : Results.NoContent();

    internal static async Task<IResult> DisableAsync(string id, PluginRuntime plugins, CancellationToken cancellationToken) =>
        await plugins.DisableAsync(id, cancellationToken) is { } error
            ? PanelResults.Error(StatusCodes.Status409Conflict, error)
            : Results.NoContent();

    internal static async Task<IResult> OrderAsync(string[] ids, PluginRuntime plugins, CancellationToken cancellationToken)
    {
        await plugins.SetOrderAsync(ids, cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> GetConfigAsync(string id, PluginRuntime plugins, CancellationToken cancellationToken) =>
        await plugins.GetConfigAsync(id, cancellationToken) is { } view
            ? Results.Ok(view)
            : PanelResults.Error(StatusCodes.Status404NotFound, "插件不存在或没有配置表单");

    internal static async Task<IResult> SaveConfigAsync(
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

    internal static async Task<IResult> GetSecretsAsync(string id, PluginRuntime plugins, CancellationToken cancellationToken)
    {
        try
        {
            return await plugins.GetSecretsAsync(id, cancellationToken) is { } fields
                ? Results.Ok(fields)
                : PanelResults.Error(StatusCodes.Status404NotFound, "插件不存在或没有机密");
        }
        catch (JsonException)
        {
            return SecretsFileInvalid(id);
        }
    }

    internal static async Task<IResult> SaveSecretsAsync(
        string            id,
        JsonObject        changes,
        PluginRuntime     plugins,
        CancellationToken cancellationToken)
    {
        try
        {
            return await plugins.SaveSecretsAsync(id, changes, cancellationToken) switch
            {
                null                         => PanelResults.Error(StatusCodes.Status404NotFound, "插件不存在或没有机密"),
                { Count: 0 }                 => Results.NoContent(),
                IReadOnlyList<string> errors => Results.Json(new { message = "机密无效", errors }, statusCode: StatusCodes.Status400BadRequest)
            };
        }
        catch (JsonException)
        {
            return SecretsFileInvalid(id);
        }
    }

    private static IResult SecretsFileInvalid(string id) =>
        PanelResults.Error(StatusCodes.Status409Conflict, $"机密文件 secrets/{id}.json 的内容与插件的机密类型不兼容，请检查或删除该文件");

    // 只在插件启用且登记了页面时提供 web/ 下的文件，路径不能越出 web/。
    internal static IResult ServeWebFile(string id, string path, PluginRuntime plugins)
    {
        if (plugins.FindActivePage(id)?.ResolveWebFile(path) is not { } file) return Results.NotFound();

        string contentType = ContentTypes.TryGetContentType(file, out string? type) ? type : "application/octet-stream";
        return Results.File(file, contentType);
    }
}
