using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.NB2Bridge.PluginPackages;
using Shinano.Plugin.NB2Bridge.Runtime;
using Shinano.Plugin.NB2Bridge.Scope;

namespace Shinano.Plugin.NB2Bridge.Panel;

/// <summary>NB2 专有面板的管理 API 路由登记，前缀由宿主规定为 <c>/api/plugin-api/nb2-bridge</c>。</summary>
public sealed class Nb2PanelApi
{
    private readonly Nb2Runner            _runner;
    private readonly Nb2Catalog           _catalog;
    private readonly Nb2Packages          _packages;
    private readonly Nb2Layout            _layout;
    private readonly Nb2ScopeStore        _scope;
    private readonly IBotAccessor         _bot;
    private readonly ILogger<Nb2PanelApi> _logger;

    /// <summary>创建路由登记。</summary>
    public Nb2PanelApi(
        Nb2Runner            runner,
        Nb2Catalog           catalog,
        Nb2Packages          packages,
        Nb2Layout            layout,
        Nb2ScopeStore        scope,
        IBotAccessor         bot,
        ILogger<Nb2PanelApi> logger)
    {
        _runner   = runner;
        _catalog  = catalog;
        _packages = packages;
        _layout   = layout;
        _scope    = scope;
        _bot      = bot;
        _logger   = logger;
    }

    /// <summary>登记全部路由。</summary>
    public void Map(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/status", () => Nb2PanelEndpoints.Status(_runner));
        routes.MapGet("/scope", () => Nb2ScopeEndpoints.Scopes(_scope));
        routes.MapPut("/scope/{kind}", (string kind, Nb2ScopeUpdate update) => Nb2ScopeEndpoints.SaveAsync(_scope, kind, update));
        routes.MapGet(
            "/scope/{kind}/candidates",
            (string kind, CancellationToken cancellationToken) => Nb2ScopeEndpoints.CandidatesAsync(_bot, _scope, kind, cancellationToken));
        routes.MapPost("/process/start", (CancellationToken cancellationToken) => Nb2PanelEndpoints.StartAsync(_runner, _logger, cancellationToken));
        routes.MapPost("/process/stop", (CancellationToken cancellationToken) => Nb2PanelEndpoints.StopAsync(_runner, _logger, cancellationToken));
        routes.MapPost("/process/restart", (CancellationToken cancellationToken) => Nb2PanelEndpoints.RestartAsync(_runner, _logger, cancellationToken));
        routes.MapGet("/logs", (int? lines) => Nb2PanelEndpoints.Logs(_runner, lines));
        routes.MapGet(
            "/plugins",
            (CancellationToken cancellationToken) => Nb2PanelEndpoints.PluginsAsync(_catalog, _runner, _logger, cancellationToken));
        routes.MapPut(
            "/plugins/{module}/enabled",
            (string module, Nb2EnabledRequest request, CancellationToken cancellationToken) =>
                Nb2PanelEndpoints.SetEnabledAsync(_catalog, _runner, module, request, _logger, cancellationToken));
        routes.MapPost(
            "/plugins",
            (Nb2InstallRequest request, CancellationToken cancellationToken) => Nb2PanelEndpoints.InstallAsync(_packages, request, cancellationToken));
        routes.MapPost(
                  "/plugins/wheel",
                  (HttpRequest request, CancellationToken cancellationToken) => Nb2PanelEndpoints.InstallWheelAsync(request, _packages, cancellationToken))
              .WithMetadata(new RequestSizeLimitAttribute(Nb2PanelEndpoints.UPLOAD_LIMIT));
        routes.MapDelete(
            "/plugins/{module}",
            (string module, CancellationToken cancellationToken) => Nb2PanelEndpoints.UninstallAsync(_packages, module, cancellationToken));
        routes.MapGet(
            "/plugins/{module}/config",
            (string module, CancellationToken cancellationToken) => Nb2PanelEndpoints.ConfigAsync(_runner, module, _logger, cancellationToken));
        routes.MapPut(
            "/plugins/{module}/config",
            (string module, JsonObject values, CancellationToken cancellationToken) =>
                Nb2PanelEndpoints.SaveConfigAsync(_runner, _layout, module, values, _logger, cancellationToken));
    }
}
