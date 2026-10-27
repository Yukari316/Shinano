using Shinano.Host.Packages;
using Shinano.Host.Restart;

namespace Shinano.Host.Panel.Restart;

/// <summary>插件包上传、待处理变更与重启。</summary>
internal static class RestartEndpoints
{
    /// <summary>插件包上传请求的大小上限：multipart 表单在文件之外还有边界与字段头，留出余量。</summary>
    internal const long UPLOAD_REQUEST_LIMIT = PluginPackageStaging.MAX_PACKAGE_BYTES + 1024 * 1024;

    /// <summary>待处理的插件包变更。</summary>
    internal static IResult Pending(PluginPackageStaging staging) => Results.Ok(staging.Pending());

    /// <summary>撤销插件的暂存变更。</summary>
    internal static IResult Undo(string id, PluginPackageStaging staging) =>
        Guard(() => staging.Undo(id) ? Results.NoContent() : PanelResults.Error(StatusCodes.Status404NotFound, "没有该插件的待处理变更"));

    /// <summary>暂存删除插件包；插件数据保留。</summary>
    internal static IResult StageRemoval(string id, PluginPackageStaging staging) =>
        Guard(() => Results.Ok(staging.StageRemoval(id)));

    /// <summary>重启状态：实例 ID、启动阶段、重启方式、待处理变更与最近一次结果。</summary>
    internal static IResult Status(RestartCoordinator coordinator, PluginPackageStaging staging) =>
        Results.Ok(
            new
            {
                instanceId = coordinator.InstanceId,
                phase      = coordinator.Phase,
                mode       = coordinator.Mode,
                restarting = coordinator.Restarting,
                pending    = staging.Pending(),
                lastResult = coordinator.LastResult
            });

    /// <summary>发起重启；返回当前实例 ID，面板据此判断已连上新进程。</summary>
    internal static IResult Restart(RestartCoordinator coordinator) =>
        coordinator.RequestRestart() is { } error
            ? PanelResults.Error(StatusCodes.Status409Conflict, error)
            : Results.Accepted(value: new { instanceId = coordinator.InstanceId });

    /// <summary>确认最近一次重启结果。</summary>
    internal static IResult DismissResult(RestartCoordinator coordinator)
    {
        coordinator.DismissResult();
        return Results.NoContent();
    }

    internal static async Task<IResult> UploadAsync(IFormFile file, PluginPackageStaging staging, CancellationToken cancellationToken)
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
