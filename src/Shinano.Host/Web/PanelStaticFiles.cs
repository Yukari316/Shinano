using Microsoft.AspNetCore.StaticFiles;

namespace Shinano.Host.Web;

/// <summary>
/// 面板静态文件的缓存策略：入口 HTML 与插件页面共享的 <c>/vendor/</c>、<c>/sdk/</c> 文件名不带内容哈希，
/// 要求浏览器每次向服务端验证，升级后不会继续使用旧版本；带哈希的构建产物照常缓存。
/// </summary>
public static class PanelStaticFiles
{
    private const string NO_CACHE = "no-cache";
    private const string HTML     = ".html";
    private const string VENDOR   = "/vendor";
    private const string SDK      = "/sdk";

    /// <summary>传给 <c>UseStaticFiles</c> 的选项。</summary>
    public static StaticFileOptions Options { get; } = new()
    {
        OnPrepareResponse = context =>
        {
            if (MustRevalidate(context.Context.Request.Path, context.File.Name))
                context.Context.Response.Headers.CacheControl = NO_CACHE;
        }
    };

    private static bool MustRevalidate(PathString path, string fileName) =>
        fileName.EndsWith(HTML, StringComparison.OrdinalIgnoreCase)
        || path.StartsWithSegments(VENDOR)
        || path.StartsWithSegments(SDK);
}
