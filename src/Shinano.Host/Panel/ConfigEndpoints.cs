using System.Text.Json.Nodes;

namespace Shinano.Host.Panel;

/// <summary>宿主配置的读取与保存；保存后需要重启生效。</summary>
internal static class ConfigEndpoints
{
    /// <summary>登记端点。</summary>
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/config", (HostConfigEditor editor) => Results.Ok(editor.Read()));
        api.MapPut("/config", Save);
    }

    private static IResult Save(JsonObject changes, HostConfigEditor editor) =>
        editor.Save(changes) is { Count: > 0 } errors
            ? Results.Json(new { message = "配置无效", errors }, statusCode: StatusCodes.Status400BadRequest)
            : Results.Ok(new { restartRequired = true });
}
