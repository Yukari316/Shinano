using Microsoft.AspNetCore.Mvc;
using Shinano.Host.Packages;
using Shinano.Host.Restart;

namespace Shinano.Host.Panel;

/// <summary>插件包上传、待处理变更与重启。</summary>
internal static class RestartEndpoints
{
    // multipart 表单在文件之外还有边界与字段头，上限留出余量。
    private const long UPLOAD_REQUEST_LIMIT = PluginPackageStaging.MAX_PACKAGE_BYTES + 1024 * 1024;

    /// <summary>登记端点。</summary>
    public static void Map(RouteGroupBuilder api)
    {
        RouteGroupBuilder packages = api.MapGroup("/packages");
        packages.MapGet("/", (PluginPackageStaging staging) => Results.Ok(staging.Pending()));
        // 会话 Cookie 为 SameSite=Strict，跨站请求不会携带会话，因此不另设防伪令牌。
        packages.MapPost("/", UploadAsync)
                .DisableAntiforgery()
                .WithMetadata(new RequestSizeLimitAttribute(UPLOAD_REQUEST_LIMIT));
        packages.MapDelete("/{id}", (string id, PluginPackageStaging staging) =>
            Guard(() => staging.Undo(id) ? Results.NoContent() : PanelResults.Error(StatusCodes.Status404NotFound, "没有该插件的待处理变更")));
        packages.MapPost("/{id}/remove", (string id, PluginPackageStaging staging) =>
            Guard(() => Results.Ok(staging.StageRemoval(id))));

        RouteGroupBuilder restart = api.MapGroup("/restart");
        restart.MapGet("/", (RestartCoordinator coordinator, PluginPackageStaging staging) =>
            Results.Ok(
                new
                {
                    instanceId = coordinator.InstanceId,
                    phase      = coordinator.Phase,
                    mode       = coordinator.Mode,
                    restarting = coordinator.Restarting,
                    pending    = staging.Pending(),
                    lastResult = coordinator.LastResult
                }));
        restart.MapPost("/", (RestartCoordinator coordinator) =>
            coordinator.RequestRestart() is { } error
                ? PanelResults.Error(StatusCodes.Status409Conflict, error)
                : Results.Accepted(value: new { instanceId = coordinator.InstanceId }));
        restart.MapDelete("/result", (RestartCoordinator coordinator) =>
        {
            coordinator.DismissResult();
            return Results.NoContent();
        });
    }

    private static async Task<IResult> UploadAsync(IFormFile file, PluginPackageStaging staging, CancellationToken cancellationToken)
    {
        if (file.Length > PluginPackageStaging.MAX_PACKAGE_BYTES)
            return PanelResults.Error(StatusCodes.Status413PayloadTooLarge, "插件包不能超过 100 MiB");

        try
        {
            await using Stream content = file.OpenReadStream();
            return Results.Ok(await staging.StageAsync(content, cancellationToken));
        }
        catch (InvalidDataException exception)
        {
            return PanelResults.Error(StatusCodes.Status400BadRequest, exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return PanelResults.Error(StatusCodes.Status409Conflict, exception.Message);
        }
    }

    // 重启开始后暂存区冻结，变更请求返回 409。
    private static IResult Guard(Func<IResult> action)
    {
        try
        {
            return action();
        }
        catch (InvalidOperationException exception)
        {
            return PanelResults.Error(StatusCodes.Status409Conflict, exception.Message);
        }
    }
}
