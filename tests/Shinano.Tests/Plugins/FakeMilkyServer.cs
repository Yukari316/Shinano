using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Shinano.Tests.Plugins;

/// <summary>
/// 测试用的最小 Milky 协议端：提供事件 WebSocket 与 HTTP API，记录 Bot 发出的消息。
/// 只实现 Shinano 测试用到的接口，其余 API 一律返回成功的空数据。
/// </summary>
public sealed class FakeMilkyServer : IAsyncDisposable
{
    /// <summary>路径前缀。</summary>
    public const string PREFIX = "milky";

    private readonly WebApplication              _app;
    private readonly ConcurrentQueue<SentMessage> _sent      = new();
    private readonly SemaphoreSlim               _sentSignal = new(0);
    private readonly TaskCompletionSource<WebSocket> _socket = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private          long                        _messageSeq = 1000;

    private FakeMilkyServer(WebApplication app, long selfId)
    {
        _app   = app;
        SelfId = selfId;
    }

    /// <summary>模拟的机器人账号。</summary>
    public long SelfId { get; }

    /// <summary>监听端口。</summary>
    public int Port { get; private set; }

    /// <summary>启动服务器，监听回环地址的随机端口。</summary>
    public static async Task<FakeMilkyServer> StartAsync(long selfId)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        WebApplication app = builder.Build();
        FakeMilkyServer server = new(app, selfId);
        app.UseWebSockets();
        app.Map($"/{PREFIX}/event", server.AcceptEventSocketAsync);
        app.MapPost($"/{PREFIX}/api/{{action}}", server.HandleApiAsync);
        await app.StartAsync();
        IServerAddressesFeature addresses =
            app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("无法取得监听地址");
        string address = addresses.Addresses.First();
        server.Port = new Uri(address).Port;
        return server;
    }

    /// <summary>推送一条群消息事件。</summary>
    public Task PushGroupMessageAsync(long groupId, long senderId, string role, string text) =>
        PushAsync(
            "message_receive",
            new JsonObject
            {
                ["message_scene"] = "group",
                ["peer_id"]       = groupId,
                ["message_seq"]   = Interlocked.Increment(ref _messageSeq),
                ["sender_id"]     = senderId,
                ["time"]          = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["segments"]      = TextSegments(text),
                ["group"] = new JsonObject
                {
                    ["group_id"]         = groupId,
                    ["group_name"]       = "test",
                    ["member_count"]     = 3,
                    ["max_member_count"] = 200
                },
                ["group_member"] = new JsonObject
                {
                    ["user_id"]        = senderId,
                    ["nickname"]       = "tester",
                    ["sex"]            = "unknown",
                    ["group_id"]       = groupId,
                    ["card"]           = "",
                    ["title"]          = "",
                    ["level"]          = 1,
                    ["role"]           = role,
                    ["join_time"]      = 0,
                    ["last_sent_time"] = 0
                }
            });

    /// <summary>推送一条好友私聊事件。</summary>
    public Task PushFriendMessageAsync(long senderId, string text) =>
        PushAsync(
            "message_receive",
            new JsonObject
            {
                ["message_scene"] = "friend",
                ["peer_id"]       = senderId,
                ["message_seq"]   = Interlocked.Increment(ref _messageSeq),
                ["sender_id"]     = senderId,
                ["time"]          = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                ["segments"]      = TextSegments(text),
                ["friend"] = new JsonObject
                {
                    ["user_id"]  = senderId,
                    ["nickname"] = "friend",
                    ["sex"]      = "unknown",
                    ["qid"]      = "",
                    ["remark"]   = ""
                }
            });

    /// <summary>推送一条群戳一戳事件。</summary>
    public Task PushGroupNudgeAsync(long groupId, long senderId, long receiverId) =>
        PushAsync(
            "group_nudge",
            new JsonObject
            {
                ["group_id"]               = groupId,
                ["sender_id"]              = senderId,
                ["receiver_id"]            = receiverId,
                ["display_action"]         = "戳了戳",
                ["display_suffix"]         = "",
                ["display_action_img_url"] = ""
            });

    /// <summary>等待下一条 Bot 发出的消息；超时返回 null。</summary>
    public async Task<SentMessage?> WaitForSentAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await _sentSignal.WaitAsync(timeout, cancellationToken)) return null;
        return _sent.TryDequeue(out SentMessage? message) ? message : null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_socket.Task.IsCompletedSuccessfully)
        {
            WebSocket socket = await _socket.Task;
            try
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // Bot 停止时会先断开连接，状态可能在检查之后才变为 Aborted；此时无需再关闭。
            }
        }

        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task PushAsync(string eventType, JsonObject data)
    {
        WebSocket socket = await _socket.Task.WaitAsync(TimeSpan.FromSeconds(10));
        JsonObject payload = new()
        {
            ["time"]       = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["self_id"]    = SelfId,
            ["event_type"] = eventType,
            ["data"]       = data
        };
        await socket.SendAsync(
            Encoding.UTF8.GetBytes(payload.ToJsonString()),
            WebSocketMessageType.Text,
            endOfMessage: true,
            CancellationToken.None);
    }

    private async Task AcceptEventSocketAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        _socket.TrySetResult(socket);
        byte[] buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            try
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close) break;
            }
            catch (Exception exception) when (exception is OperationCanceledException or WebSocketException)
            {
                break;
            }
        }
    }

    private async Task<IResult> HandleApiAsync(string action, HttpContext context)
    {
        JsonNode? input = await JsonNode.ParseAsync(context.Request.Body);
        JsonNode data = action switch
        {
            "get_login_info" => new JsonObject
            {
                ["uin"]      = SelfId,
                ["nickname"] = "shinano-test"
            },
            "send_group_message" or "send_private_message" => RecordSent(action, input),
            _ => new JsonObject()
        };
        return Results.Text(
            new JsonObject
            {
                ["status"]  = "ok",
                ["retcode"] = 0,
                ["data"]    = data
            }.ToJsonString(),
            "application/json");
    }

    private JsonObject RecordSent(string action, JsonNode? input)
    {
        bool group = action == "send_group_message";
        // SAFE: Sora 发送 API 总会提交目标 ID 与消息段。
        long target = input![group ? "group_id" : "user_id"]!.GetValue<long>();
        string text = string.Concat(
            input["message"]!.AsArray()
                             .Where(segment => segment?["type"]?.GetValue<string>() == "text")
                             .Select(segment => segment!["data"]!["text"]!.GetValue<string>()));
        int segments = input["message"]!.AsArray().Count;
        _sent.Enqueue(
            new SentMessage
            {
                IsGroup      = group,
                TargetId     = target,
                Text         = text,
                SegmentCount = segments,
                Raw          = input.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
            });
        _sentSignal.Release();
        return new JsonObject
        {
            ["message_seq"] = Interlocked.Increment(ref _messageSeq),
            ["time"]        = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
    }

    private static JsonArray TextSegments(string text) =>
    [
        new JsonObject
        {
            ["type"] = "text",
            ["data"] = new JsonObject { ["text"] = text }
        }
    ];
}
