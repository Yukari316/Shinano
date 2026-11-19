using System.Text.Json.Nodes;

namespace Shinano.Host.Panel.HostConfig;

/// <summary>宿主配置的读取与保存；保存后需要重启生效。</summary>
internal static class ConfigEndpoints
{
    /// <summary>读取全部宿主配置项；敏感项只报告是否已配置。</summary>
    internal static IResult Read(HostConfigEditor editor) => Results.Ok(editor.Read());

    internal static async Task<IResult> SaveAsync(JsonObject changes, HostConfigEditor editor, CancellationToken cancellationToken)
    {
        HostConfigSaveResult result = await editor.SaveAsync(changes, cancellationToken);
        return result.Errors.Count > 0
            ? Results.Json(new { message = "配置无效", errors = result.Errors }, statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(new { restartRequired = true, notes = result.Notes });
    }
}
