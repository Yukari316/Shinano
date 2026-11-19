using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili.Feed;
using Shinano.Plugin.Bilibili.Login;
using Shinano.Plugin.Bilibili.Persistence;

namespace Shinano.Plugin.Bilibili.Panel;

/// <summary>
/// B站订阅专有面板的管理 API 路由登记，前缀由宿主规定为 <c>/api/plugin-api/bilibili</c>。插件服务在此闭包传给处理方法。
/// </summary>
public sealed class BiliPanelApi
{
    private readonly BiliSession                  _session;
    private readonly BiliLogin                    _login;
    private readonly BiliStore                    _store;
    private readonly BiliPoller                   _poller;
    private readonly IPluginInitializationContext _context;
    private readonly ILogger<BiliPanelApi>        _logger;

    /// <summary>创建路由登记。</summary>
    public BiliPanelApi(
        BiliSession                  session,
        BiliLogin                    login,
        BiliStore                    store,
        BiliPoller                   poller,
        IPluginInitializationContext context,
        ILogger<BiliPanelApi>        logger)
    {
        _session = session;
        _login   = login;
        _store   = store;
        _poller  = poller;
        _context = context;
        _logger  = logger;
    }

    /// <summary>登记全部路由。</summary>
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/status", () => BiliPanelEndpoints.Status(_session));
        routes.MapPost(
            "/login",
            (BiliLoginRequest request, CancellationToken cancellationToken) =>
                BiliPanelEndpoints.StartLoginAsync(_session, _login, request, _logger, cancellationToken));
        routes.MapGet(
            "/login/{key}",
            (string key, CancellationToken cancellationToken) => BiliPanelEndpoints.PollLoginAsync(_login, _session, _poller, key, cancellationToken));
        routes.MapGet("/follows", () => BiliPanelEndpoints.FollowsAsync(_session, _store));
        routes.MapPost(
            "/follows/refresh",
            (CancellationToken cancellationToken) => BiliPanelEndpoints.RefreshFollowsAsync(_poller, _session, _store, cancellationToken));
        routes.MapPut(
            "/follows/{uid:long}",
            (long uid, BiliSubscriptionUpdate update) => BiliPanelEndpoints.SaveSubscriptionAsync(_session, _store, _poller, uid, update, _logger));
        routes.MapGet("/groups", () => BiliPanelEndpoints.GroupsAsync(_context, _store));
    }
}
