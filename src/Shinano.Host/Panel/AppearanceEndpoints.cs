using Microsoft.AspNetCore.StaticFiles;
using Shinano.Infrastructure.Media;

namespace Shinano.Host.Panel;

/// <summary>面板外观：背景图、不透明度与磨砂模糊。</summary>
internal static class AppearanceEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>登记端点。</summary>
    public static void Map(RouteGroupBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("/appearance");
        group.MapGet("/", (AppearanceStore store) => Results.Ok(store.Read()));
        group.MapPut("/", (AppearanceUpdate update, AppearanceStore store) =>
        {
            store.Apply(update);
            return Results.NoContent();
        });
        group.MapGet("/background", ServeBackground);
        // 会话 Cookie 为 SameSite=Strict，跨站请求不会携带会话，因此不另设防伪令牌。
        group.MapPost("/background", UploadAsync).DisableAntiforgery();
        group.MapDelete("/background", (AppearanceStore store) =>
        {
            store.DeleteBackground();
            return Results.NoContent();
        });
    }

    private static IResult ServeBackground(AppearanceStore store)
    {
        if (store.BackgroundPath() is not { } path) return Results.NotFound();

        string contentType = ContentTypes.TryGetContentType(path, out string? type) ? type : "application/octet-stream";
        return Results.File(path, contentType);
    }

    private static async Task<IResult> UploadAsync(IFormFile file, AppearanceStore store, CancellationToken cancellationToken)
    {
        if (file.Length > AppearanceStore.MAX_BACKGROUND_BYTES)
            return PanelResults.Error(StatusCodes.Status413PayloadTooLarge, "背景图不能超过 10 MiB");

        try
        {
            await using Stream content = file.OpenReadStream();
            return await store.SaveBackgroundAsync(content, cancellationToken) is { } error
                ? PanelResults.Error(StatusCodes.Status400BadRequest, error)
                : Results.NoContent();
        }
        catch (MediaTooLargeException)
        {
            return PanelResults.Error(StatusCodes.Status413PayloadTooLarge, "背景图不能超过 10 MiB");
        }
    }
}
