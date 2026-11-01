using System.Text.Json.Nodes;

namespace Shinano.Host.Panel.Endpoints;

/// <summary>宿主配置的读取与保存；保存后需要重启生效。</summary>
internal static class ConfigEndpoints
{
    /// <summary>读取全部宿主配置项；敏感项只报告是否已配置。</summary>
    internal static IResult Read(HostConfigEditor editor) => Results.Ok(editor.Read());

    internal static IResult Save(JsonObject changes, HostConfigEditor editor) =>
        editor.Save(changes) is { Count: > 0 } errors
            ? Results.Json(new { message = "配置无效", errors }, statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(new { restartRequired = true });
}
