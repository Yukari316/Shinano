using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Hso.Endpoints;
using Shinano.Plugin.Hso.Models;

namespace Shinano.Plugin.Hso;

/// <summary>
/// Hso 专有面板的管理 API 路由登记，前缀由宿主规定为 <c>/api/plugin-api/hso</c>。
/// 请求的服务来自宿主容器，插件服务在此闭包传给处理方法。
/// </summary>
public sealed class HsoPanelApi
{
    private readonly HsoGallery                   _gallery;
    private readonly HsoGroups                    _groups;
    private readonly IPluginInitializationContext _context;
    private readonly ILogger<HsoPanelApi>         _logger;

    /// <summary>创建路由登记。</summary>
    public HsoPanelApi(HsoGallery gallery, HsoGroups groups, IPluginInitializationContext context, ILogger<HsoPanelApi> logger)
    {
        _gallery = gallery;
        _groups  = groups;
        _context = context;
        _logger  = logger;
    }

    /// <summary>登记全部路由。</summary>
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/groups", () => HsoPanelEndpoints.GroupsAsync(_context, _groups));
        routes.MapPut("/groups/{groupId:long}", (long groupId, HsoGroupUpdate update) => HsoPanelEndpoints.SaveGroupAsync(_groups, groupId, update));
        routes.MapGet(
            "/images",
            (int? page, bool? nsfw, bool? hasPid, bool? blocked, string? keyword) =>
                HsoPanelEndpoints.ListAsync(_gallery, page, nsfw, hasPid, blocked, keyword));
        routes.MapPost(
                  "/images",
                  (HttpRequest request, CancellationToken cancellationToken) => HsoPanelEndpoints.UploadAsync(request, _gallery, _logger, cancellationToken))
              .WithMetadata(new RequestSizeLimitAttribute(HsoPanelEndpoints.UPLOAD_LIMIT));
        routes.MapGet(
            "/images/{id:long}/thumbnail",
            (long id, CancellationToken cancellationToken) => HsoPanelEndpoints.ThumbnailAsync(_gallery, id, cancellationToken));
        routes.MapGet(
            "/images/{id:long}/original",
            (long id, CancellationToken cancellationToken) => HsoPanelEndpoints.OriginalAsync(_gallery, id, cancellationToken));
        routes.MapPut("/images/{id:long}/nsfw", (long id, HsoNsfwUpdate update) => HsoPanelEndpoints.SetNsfwAsync(_gallery, id, update));
        routes.MapDelete("/images/{id:long}/blocked", (long id) => HsoPanelEndpoints.ClearBlockedAsync(_gallery, id));
        routes.MapDelete(
            "/images/{id:long}",
            (long id, CancellationToken cancellationToken) => HsoPanelEndpoints.DeleteAsync(_gallery, id, cancellationToken));
    }
}
