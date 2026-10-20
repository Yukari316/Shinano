using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Shinano.Host.Panel;

/// <summary>登录、退出、会话检查与重新生成 Token。</summary>
internal static class AuthEndpoints
{
    /// <summary>登记端点。</summary>
    public static void Map(IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/auth");
        group.MapPost("/login", LoginAsync);
        // 只接收 HttpContext 的方法会被当作 RequestDelegate 绑定并丢弃返回值，转为 Delegate 才会写出返回的结果。
        group.MapPost("/logout", (Delegate)LogoutAsync);
        group.MapGet("/session", () => Results.NoContent()).RequireAuthorization();
        group.MapPost("/token", Regenerate).RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest    request,
        HttpContext     context,
        PanelTokenStore tokens,
        LoginThrottle   throttle)
    {
        IPAddress address = context.Connection.RemoteIpAddress ?? IPAddress.None;
        if (throttle.IsBlocked(address))
            return PanelResults.Error(StatusCodes.Status429TooManyRequests, "登录失败次数过多，请稍后再试");
        if (!tokens.Verify(request.Token))
        {
            throttle.RecordFailure(address);
            return PanelResults.Error(StatusCodes.Status401Unauthorized, "Token 不正确");
        }

        throttle.Reset(address);
        await PanelAuthentication.SignInAsync(context, tokens);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    // 新 Token 只在此响应中出现一次；当前会话随旧 Token 一起失效。
    private static IResult Regenerate(PanelTokenStore tokens) => Results.Ok(new { token = tokens.Regenerate() });
}
