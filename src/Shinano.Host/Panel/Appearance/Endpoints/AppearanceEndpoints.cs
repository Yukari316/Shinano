using Microsoft.AspNetCore.StaticFiles;
using Shinano.Host.Panel.Appearance.Models;
using Shinano.Infrastructure.Media;

namespace Shinano.Host.Panel.Appearance.Endpoints;

/// <summary>面板外观：背景图、不透明度与磨砂模糊。</summary>
internal static class AppearanceEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    /// <summary>读取外观设置。</summary>
    internal static IResult Get(AppearanceStore store) => Results.Ok(store.Read());

    /// <summary>保存不透明度与模糊设置。</summary>
    internal static IResult Update(AppearanceUpdate update, AppearanceStore store)
    {
        store.Apply(update);
        return Results.NoContent();
    }

    /// <summary>删除背景图。</summary>
    internal static IResult DeleteBackground(AppearanceStore store)
    {
        store.DeleteBackground();
        return Results.NoContent();
    }

    internal static IResult ServeBackground(AppearanceStore store)
    {
        if (store.BackgroundPath() is not { } path) return Results.NotFound();

        string contentType = ContentTypes.TryGetContentType(path, out string? type) ? type : "application/octet-stream";
        return Results.File(path, contentType);
    }

    internal static async Task<IResult> UploadAsync(IFormFile file, AppearanceStore store, CancellationToken cancellationToken)
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
