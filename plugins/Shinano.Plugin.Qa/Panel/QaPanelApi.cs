using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Qa.Persistence;

namespace Shinano.Plugin.Qa.Panel;

/// <summary>
/// 有人问专有面板的管理 API 路由登记，前缀由宿主规定为 <c>/api/plugin-api/qa</c>。
/// 请求的服务来自宿主容器，插件服务在此闭包传给处理方法。
/// </summary>
public sealed class QaPanelApi
{
    /// <summary>新增词条请求的大小上限：回答可含多个媒体，留出表单开销的余量。</summary>
    private const long UPLOAD_REQUEST_LIMIT = 256L * 1024 * 1024;

    private readonly QaStore                      _store;
    private readonly QaMediaStore                 _media;
    private readonly IPluginInitializationContext _context;
    private readonly ILogger<QaPanelApi>          _logger;

    /// <summary>创建路由登记。</summary>
    public QaPanelApi(QaStore store, QaMediaStore media, IPluginInitializationContext context, ILogger<QaPanelApi> logger)
    {
        _store   = store;
        _media   = media;
        _context = context;
        _logger  = logger;
    }

    /// <summary>登记全部路由。</summary>
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/groups", () => QaPanelEndpoints.GroupsAsync(_context));
        routes.MapGet(
            "/entries",
            (long? groupId, QaMatchType? matchType, string? keyword) => QaPanelEndpoints.ListAsync(_store, groupId, matchType, keyword));
        routes.MapPost(
                  "/entries",
                  (HttpRequest request, CancellationToken cancellationToken) =>
                      QaPanelEndpoints.AddAsync(request, _store, _media, _context, _logger, cancellationToken))
              .WithMetadata(new RequestSizeLimitAttribute(UPLOAD_REQUEST_LIMIT));
        routes.MapDelete("/entries/{id:long}", (long id) => QaPanelEndpoints.DeleteAsync(_store, _media, _logger, id));
        routes.MapGet(
            "/media/{id:long}",
            (long id, CancellationToken cancellationToken) => QaPanelEndpoints.MediaAsync(_store, _context, id, cancellationToken));
        routes.MapGet("/forbidden", () => QaPanelEndpoints.ForbiddenAsync(_store));
        routes.MapPost("/forbidden", (QaForbiddenRequest request) => QaPanelEndpoints.AddForbiddenAsync(_store, request));
        routes.MapDelete("/forbidden/{id:long}", (long id) => QaPanelEndpoints.DeleteForbiddenAsync(_store, id));
    }
}
