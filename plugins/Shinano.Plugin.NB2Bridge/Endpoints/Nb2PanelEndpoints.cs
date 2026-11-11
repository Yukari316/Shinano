using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;
using Shinano.Plugin.NB2Bridge.Models;

namespace Shinano.Plugin.NB2Bridge.Endpoints;

/// <summary>NB2 专有面板 API 的处理方法，路由见 <see cref="Nb2PanelApi" />。</summary>
internal static class Nb2PanelEndpoints
{
    /// <summary>上传 wheel 的大小上限。</summary>
    internal const long UPLOAD_LIMIT = 512L * 1024 * 1024;

    private const int DEFAULT_LOG_LINES = 200;

    private static readonly Dictionary<string, JsonValueKind[]> SchemaKinds = new()
    {
        ["string"]  = [JsonValueKind.String],
        ["integer"] = [JsonValueKind.Number],
        ["number"]  = [JsonValueKind.Number],
        ["boolean"] = [JsonValueKind.True, JsonValueKind.False],
        ["array"]   = [JsonValueKind.Array],
        ["object"]  = [JsonValueKind.Object]
    };

    /// <summary>进程状态。</summary>
    internal static IResult Status(Nb2Runner runner) => Results.Ok(runner.Status());

    /// <summary>启动 NB2。</summary>
    internal static async Task<IResult> StartAsync(Nb2Runner runner, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("面板请求启动 NB2");
        await runner.StartAsync(cancellationToken);
        return Results.Ok(runner.Status());
    }

    /// <summary>停止 NB2。</summary>
    internal static async Task<IResult> StopAsync(Nb2Runner runner, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("面板请求停止 NB2");
        await runner.StopAsync(cancellationToken);
        return Results.Ok(runner.Status());
    }

    /// <summary>重启 NB2。</summary>
    internal static async Task<IResult> RestartAsync(Nb2Runner runner, ILogger logger, CancellationToken cancellationToken)
    {
        logger.LogInformation("面板请求重启 NB2");
        await runner.RestartAsync(cancellationToken);
        return Results.Ok(runner.Status());
    }

    /// <summary>NB2 进程输出的最近若干行。</summary>
    internal static IResult Logs(Nb2Runner runner, int? lines) =>
        Results.Ok(runner.RecentLog(Math.Clamp(lines ?? DEFAULT_LOG_LINES, 1, Nb2LogSink.RECENT_CAPACITY)));

    /// <summary>已安装的插件与运行时状态。</summary>
    internal static async Task<IResult> PluginsAsync(Nb2Catalog catalog, Nb2Runner runner, ILogger logger, CancellationToken cancellationToken)
    {
        IReadOnlyList<Nb2RemotePlugin> remote = await RemoteAsync(runner, logger, cancellationToken);
        return Results.Ok(
            catalog.Entries()
                   .Select(entry => new Nb2PluginView
                   {
                       Package     = entry.Package,
                       Module      = entry.Module,
                       Version     = entry.Version,
                       FromWheel   = entry.Wheel is not null,
                       Enabled     = entry.Enabled,
                       InstalledAt = entry.InstalledAt,
                       Remote = remote.FirstOrDefault(item => item.Module == entry.Module) is { } live
                           ? new Nb2RemoteView
                           {
                               Loaded      = live.Loaded,
                               Name        = live.Name,
                               Description = live.Description,
                               Matchers    = live.Matchers,
                               HasConfig   = live.ConfigSchema is not null,
                               Stats       = live.Stats
                           }
                           : null
                   }));
    }

    /// <summary>启用或停止插件的事件响应，即时推送给运行中的 NB2。</summary>
    internal static async Task<IResult> SetEnabledAsync(
        Nb2Catalog        catalog,
        Nb2Runner         runner,
        string            module,
        Nb2EnabledRequest request,
        ILogger           logger,
        CancellationToken cancellationToken)
    {
        if (!catalog.SetEnabled(module, request.Enabled)) return Results.NotFound(new { error = $"没有安装模块为 {module} 的插件" });

        logger.LogInformation("NB2 插件 {Module} 已{Action}事件响应", module, request.Enabled ? "启用" : "停止");
        try
        {
            await runner.PushDisabledAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // 状态已保存，NB2 下次连上时会推送。
            logger.LogWarning(exception, "推送 NB2 禁用集合失败");
        }

        return Results.NoContent();
    }

    /// <summary>从 PyPI 安装插件。</summary>
    internal static async Task<IResult> InstallAsync(Nb2Packages packages, Nb2InstallRequest request, CancellationToken cancellationToken) =>
        await packages.InstallAsync(request.Package.Trim(), request.Version.Trim(), request.Module, cancellationToken) is { } error
            ? Results.BadRequest(new { error })
            : Results.NoContent();

    /// <summary>上传 wheel 安装插件；表单字段 <c>file</c> 与可选的 <c>module</c>。</summary>
    internal static async Task<IResult> InstallWheelAsync(HttpRequest request, Nb2Packages packages, CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType) return Results.BadRequest(new { error = "请上传 wheel 文件" });
        // 默认的 multipart 上限为 128 MB；处理方法直接读取表单，路由元数据不作用于此，在读取前显式设定。
        request.HttpContext.Features.Set<IFormFeature>(new FormFeature(request, new FormOptions { MultipartBodyLengthLimit = UPLOAD_LIMIT }));
        IFormCollection form = await request.ReadFormAsync(cancellationToken);
        if (form.Files.FirstOrDefault() is not { } file) return Results.BadRequest(new { error = "请上传 wheel 文件" });

        await using Stream content = file.OpenReadStream();
        return await packages.InstallWheelAsync(file.FileName, content, form["module"].ToString(), cancellationToken) is { } error
            ? Results.BadRequest(new { error })
            : Results.NoContent();
    }

    /// <summary>卸载插件。</summary>
    internal static async Task<IResult> UninstallAsync(Nb2Packages packages, string module, CancellationToken cancellationToken) =>
        await packages.UninstallAsync(module, cancellationToken) is { } error
            ? Results.BadRequest(new { error })
            : Results.NoContent();

    /// <summary>插件配置表单：只有 NB2 运行时能读取配置模型。</summary>
    internal static async Task<IResult> ConfigAsync(Nb2Runner runner, string module, ILogger logger, CancellationToken cancellationToken)
    {
        if (!runner.IsReady) return Results.Conflict(new { error = "NB2 未运行，无法读取配置模型" });
        if ((await RemoteAsync(runner, logger, cancellationToken)).FirstOrDefault(item => item.Module == module) is not { } plugin)
            return Results.NotFound(new { error = $"NB2 中没有加载 {module}" });
        if (plugin is not { ConfigSchema: { } schema, Config: { } values }) return Results.NotFound(new { error = "该插件没有可配置项" });

        return Results.Ok(
            new Nb2ConfigView
            {
                Schema = schema,
                Values = values
            });
    }

    /// <summary>
    /// 保存插件配置到 <c>.env</c>：只接受配置模型中的字段，按 schema 的类型检查值（null 表示恢复默认）；导入时读取的配置需要重启 NB2 生效。
    /// </summary>
    internal static async Task<IResult> SaveConfigAsync(
        Nb2Runner         runner,
        Nb2Layout         layout,
        string            module,
        JsonObject        values,
        ILogger           logger,
        CancellationToken cancellationToken)
    {
        if (!runner.IsReady) return Results.Conflict(new { error = "NB2 未运行，无法读取配置模型" });
        if ((await RemoteAsync(runner, logger, cancellationToken)).FirstOrDefault(item => item.Module == module) is not { ConfigSchema: { } schema })
            return Results.NotFound(new { error = "该插件没有可配置项" });

        JsonObject properties = schema["properties"] as JsonObject ?? [];
        Dictionary<string, JsonNode?> changes = [];
        foreach ((string field, JsonNode? value) in values)
        {
            if (properties[field] is not JsonObject property) return Results.BadRequest(new { error = $"配置模型中没有 {field}" });
            if (value is not null && !Accepts(property, value.GetValueKind()))
                return Results.BadRequest(new { error = $"{field} 的值类型不符合配置模型" });
            changes[field] = value?.DeepClone();
        }

        Nb2DotEnv.Save(layout.EnvFile, changes);
        logger.LogInformation("已保存 NB2 插件 {Module} 的配置：{Fields}，重启 NB2 后生效", module, string.Join("、", changes.Keys));
        return Results.Ok(new { restartRequired = true });
    }

    private static async Task<IReadOnlyList<Nb2RemotePlugin>> RemoteAsync(Nb2Runner runner, ILogger logger, CancellationToken cancellationToken)
    {
        if (!runner.IsReady || runner.Control is not { } control) return [];
        try
        {
            return await control.PluginsAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "读取 NB2 插件状态失败");
            return [];
        }
    }

    // 字段的类型来自 type（字符串或数组）或 anyOf 各分支的 type；引用与枚举等无法直接判断的不检查，由 NoneBot 启动时校验。
    private static bool Accepts(JsonObject property, JsonValueKind kind)
    {
        string[] types =
        [
            .. TypesOf(property),
            .. (property["anyOf"] as JsonArray ?? []).OfType<JsonObject>().SelectMany(TypesOf)
        ];
        string[] known = [.. types.Where(SchemaKinds.ContainsKey)];
        return known.Length == 0 || known.Any(type => SchemaKinds[type].Contains(kind));
    }

    private static IEnumerable<string> TypesOf(JsonObject schema) =>
        schema["type"] switch
        {
            JsonValue single when single.TryGetValue(out string? type) => [type],
            JsonArray many => many.OfType<JsonValue>().Select(item => item.TryGetValue(out string? type) ? type : null).OfType<string>(),
            _ => []
        };
}
