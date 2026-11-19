using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace Shinano.Host.Panel.Auth;

/// <summary>
/// 面板会话：以 Token 换取 HttpOnly、SameSite=Strict 的 Cookie，有效期 7 天，活动时续期；
/// 签名密钥持久化到部署数据目录，重启后无需重新登录。会话记录 Token 指纹，Token 更换后会话失效。
/// </summary>
public static class PanelAuthentication
{
    /// <summary>会话 Cookie 名。</summary>
    public const string COOKIE_NAME = "shinano_session";

    /// <summary>会话中记录 Token 指纹的声明类型。</summary>
    public const string FINGERPRINT_CLAIM = "shinano:token";

    private const string KEYS_DIRECTORY_NAME = "keys";
    private const string APPLICATION_NAME    = "Shinano";

    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);

    /// <summary>登记面板认证服务。</summary>
    /// <param name="services">服务集合。</param>
    /// <param name="dataDirectory">部署数据目录，签名密钥保存在其中的 <c>keys</c>。</param>
    public static IServiceCollection AddPanelAuthentication(this IServiceCollection services, string dataDirectory)
    {
        services.AddDataProtection()
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, KEYS_DIRECTORY_NAME)))
                .SetApplicationName(APPLICATION_NAME);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
                .AddCookie(options =>
                {
                    options.Cookie.Name         = COOKIE_NAME;
                    options.Cookie.HttpOnly     = true;
                    options.Cookie.SameSite     = SameSiteMode.Strict;
                    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.ExpireTimeSpan      = SessionLifetime;
                    options.SlidingExpiration   = true;
                    // 面板是单页应用，未登录与无权限直接返回状态码，由前端跳转登录页。
                    options.Events.OnRedirectToLogin = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    };
                    options.Events.OnRedirectToAccessDenied = context =>
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    };
                    options.Events.OnValidatePrincipal = ValidateFingerprintAsync;
                });
        services.AddAuthorization();
        return services;
    }

    /// <summary>以当前 Token 指纹登录。</summary>
    public static Task SignInAsync(HttpContext context, PanelTokenStore tokens) =>
        context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(FINGERPRINT_CLAIM, tokens.Fingerprint)],
                    CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties { IsPersistent = true });

    private static async Task ValidateFingerprintAsync(CookieValidatePrincipalContext context)
    {
        PanelTokenStore tokens = context.HttpContext.RequestServices.GetRequiredService<PanelTokenStore>();
        if (context.Principal?.FindFirst(FINGERPRINT_CLAIM)?.Value == tokens.Fingerprint) return;

        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
