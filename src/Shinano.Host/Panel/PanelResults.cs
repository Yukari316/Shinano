using Shinano.Infrastructure.Hosting;

namespace Shinano.Host.Panel;

/// <summary>面板 API 的通用响应。错误响应体为 <c>{ "message": "..." }</c>。</summary>
internal static class PanelResults
{
    /// <summary>带说明的错误响应。</summary>
    public static IResult Error(int statusCode, string message) => Results.Json(new { message }, statusCode: statusCode);

    /// <summary>基础设施处于故障模式或尚未就绪。</summary>
    public static IResult Unavailable() => Error(StatusCodes.Status503ServiceUnavailable, "基础设施未就绪或处于故障模式");

    /// <summary>基础设施是否已就绪。</summary>
    public static bool IsReady(HostInfrastructure infrastructure) =>
        infrastructure.Ready is { IsCompletedSuccessfully: true, Result: true };
}
