using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Shinano.Host.Panel.Auth;

/// <summary>登录、退出、会话检查与重新生成 Token。</summary>
internal static class AuthEndpoints
{
    /// <summary>当前会话有效时返回 204，由路由的鉴权要求拒绝无效会话。</summary>
    internal static IResult Session() => Results.NoContent();

    internal static async Task<IResult> LoginAsync(
        LoginRequest    request,
        HttpContext     context,
        PanelTokenStore tokens,
        LoginThrottle   throttle)
    {
        IPAddress address = context.Connection.RemoteIpAddress ?? IPAddress.None;
        if (throttle.IsBlocked(address))
            return PanelResults.Error(StatusCodes.Status429TooManyRequests, "登录失败次数过多，请稍后再试");
        // JSON 中的 null 会绕过属性默认值，空 Token 与错误 Token 同样计入失败。
        if (string.IsNullOrEmpty(request.Token) || !tokens.Verify(request.Token))
        {
            throttle.RecordFailure(address);
            return PanelResults.Error(StatusCodes.Status401Unauthorized, "Token 不正确");
        }

        throttle.Reset(address);
        await PanelAuthentication.SignInAsync(context, tokens);
        return Results.NoContent();
    }

    internal static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.NoContent();
    }

    // 新 Token 只在此响应中出现一次；当前会话随旧 Token 一起失效。
    internal static IResult Regenerate(PanelTokenStore tokens) => Results.Ok(new { token = tokens.Regenerate() });
}

/// <summary>登录请求。</summary>
public sealed class LoginRequest
{
    /// <summary>面板 Token。</summary>
    public string Token { get; set; } = string.Empty;
}
