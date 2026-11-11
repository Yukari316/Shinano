using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins.Models;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>
/// NB2Bridge 扮演的 Milky 实现端，只监听 127.0.0.1 的随机端口：<c>GET /event</c> 为事件 WebSocket，<c>POST /api/{action}</c> 为 API。
/// 两者都要求本次启动生成的 token（WebSocket 取查询参数 <c>access_token</c> 或 Bearer，API 取 Bearer）。
/// 同一时刻只保留一个事件连接，新连接替换旧连接。用独立的空 WebApplication，不读取宿主配置，避免继承宿主的 Kestrel 端点设置。
/// </summary>
public sealed class MilkyServer : IAsyncDisposable
{
    private const string BEARER       = "Bearer ";
    private const string TOKEN_QUERY  = "access_token";
    private const string JSON         = "application/json";
    private const int    BUFFER_BYTES = 4096;

    private readonly MilkyActions  _actions;
    private readonly ILogger       _logger;
    private readonly byte[]        _token;
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly WebApplication _app;
    private volatile WebSocket?    _socket;

    private MilkyServer(MilkyActions actions, string token, ILogger logger)
    {
        _actions = actions;
        _token   = Encoding.UTF8.GetBytes(token);
        _logger  = logger;

        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.WebHost.UseKestrelCore().ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddRouting();
        _app = builder.Build();
        _app.UseWebSockets();
        _app.Map("/event", AcceptEventsAsync);
        _app.MapPost("/api/{action}", (string action, HttpContext context) => CallAsync(action, context));
    }

    /// <summary>事件连接建立。</summary>
    public event Action? Connected;

    /// <summary>事件连接断开。</summary>
    public event Action? Disconnected;

    /// <summary>监听的端口。</summary>
    public int Port { get; private set; }

    /// <summary>是否有事件连接。</summary>
    public bool IsConnected => _socket is { State: WebSocketState.Open };

    /// <summary>启动服务并取得端口。</summary>
    public static async Task<MilkyServer> StartAsync(MilkyActions actions, string token, ILogger logger, CancellationToken cancellationToken)
    {
        MilkyServer server = new(actions, token, logger);
        await server._app.StartAsync(cancellationToken);
        string address = server._app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.First();
        server.Port = new Uri(address).Port;
        return server;
    }

    /// <summary>经事件连接发送一个事件。</summary>
    /// <exception cref="InvalidOperationException">没有事件连接。</exception>
    public async Task SendAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        WebSocket socket = _socket ?? throw new InvalidOperationException("NB2 未连接");
        byte[] bytes = Encoding.UTF8.GetBytes(payload.ToJsonString());
        await _send.WaitAsync(cancellationToken);
        try
        {
            await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
        }
        finally
        {
            _send.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _socket?.Abort();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private bool Authorized(HttpContext context, bool allowQuery)
    {
        string? token = context.Request.Headers.Authorization.ToString() is { } header && header.StartsWith(BEARER, StringComparison.Ordinal)
            ? header[BEARER.Length..]
            : allowQuery ? context.Request.Query[TOKEN_QUERY].ToString() : null;
        return token is { Length: > 0 } && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), _token);
    }

    private async Task AcceptEventsAsync(HttpContext context)
    {
        if (!Authorized(context, allowQuery: true))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        _socket?.Abort();
        _socket = socket;
        _logger.LogInformation("NB2 已连接事件通道");
        Connected?.Invoke();
        try
        {
            // 适配器不经事件通道发送内容，读取只为感知关闭。
            byte[] buffer = new byte[BUFFER_BYTES];
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            _logger.LogDebug(exception, "NB2 事件通道异常结束");
        }
        finally
        {
            if (ReferenceEquals(_socket, socket))
            {
                _socket = null;
                _logger.LogWarning("NB2 事件通道已断开");
                Disconnected?.Invoke();
            }
        }
    }

    private async Task<IResult> CallAsync(string action, HttpContext context)
    {
        if (!Authorized(context, allowQuery: false)) return Results.Unauthorized();

        JsonObject parameters;
        try
        {
            parameters = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted) as JsonObject ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return Results.BadRequest();
        }

        MilkyCallResult result = await _actions.HandleAsync(action, parameters, context.RequestAborted);
        return Results.Content(result.Body, JSON, Encoding.UTF8, result.StatusCode);
    }
}
