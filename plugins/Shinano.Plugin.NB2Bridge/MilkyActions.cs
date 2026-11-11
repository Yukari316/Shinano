using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Framework.Plugins.Models;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>
/// 执行 NB2 经 Milky API 发起的调用：发送群聊与私聊消息转为 Sora 发送（成功后由宿主记录器归档）；
/// <see cref="EVENT_DONE" /> 是控制插件的处理结果回报；其余动作原样转发给 LLBot 并原样返回。
/// 参数中的本地文件先上传换成预签名链接。
/// </summary>
public sealed class MilkyActions
{
    /// <summary>控制插件回报事件处理结果的动作名。</summary>
    public const string EVENT_DONE = "shinano_event_done";

    private const string SEND_GROUP   = "send_group_message";
    private const string SEND_PRIVATE = "send_private_message";
    private const string MESSAGE      = "message";
    private const int    OK_STATUS    = 200;
    private const int    FAILED_CODE  = -1;

    private readonly IPluginInitializationContext _context;
    private readonly EventTracker                 _tracker;
    private readonly LocalFiles                   _files;
    private readonly TimeProvider                 _time;
    private readonly ILogger<MilkyActions>        _logger;

    /// <summary>创建动作执行器。</summary>
    public MilkyActions(
        IPluginInitializationContext context,
        EventTracker                 tracker,
        LocalFiles                   files,
        TimeProvider                 time,
        ILogger<MilkyActions>        logger)
    {
        _context = context;
        _tracker = tracker;
        _files   = files;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>执行一个动作，返回给 NB2 的 HTTP 状态码与响应正文；失败以 Milky 的 failed 响应表示，不抛出。</summary>
    public async Task<MilkyCallResult> HandleAsync(string action, JsonObject parameters, CancellationToken cancellationToken)
    {
        try
        {
            switch (action)
            {
                case EVENT_DONE:
                {
                    string key = parameters["key"]?.GetValue<string>() ?? string.Empty;
                    bool handled = parameters["handled"]?.GetValue<bool>() ?? false;
                    if (!_tracker.Complete(key, handled))
                    {
                        _logger.LogWarning(
                            "NB2 在等待超时后才回报{Verdict}（关联键 {Key}），该事件已按继续交给后续订阅者",
                            handled ? "开始处理" : "不处理",
                            key);
                    }

                    return Ok([]);
                }
                case SEND_GROUP or SEND_PRIVATE:
                    return await SendAsync(action, parameters, cancellationToken);
                default:
                    await _files.RewriteAsync(parameters, cancellationToken);
                    return await _context.Bot.CallMilkyAsync(action, parameters, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "执行 NB2 的 Milky 调用 {Action} 失败", action);
            return Failed(exception.Message);
        }
    }

    private async Task<MilkyCallResult> SendAsync(string action, JsonObject parameters, CancellationToken cancellationToken)
    {
        if (_context.Bot.Api is not { } api) return Failed("LLBot 未连接");

        JsonArray segments = parameters[MESSAGE] as JsonArray ?? [];
        await _files.RewriteAsync(segments, cancellationToken);
        List<string> dropped = [];
        MessageBody body = MilkySegments.FromMilky(segments, dropped);
        if (dropped.Count > 0) _logger.LogWarning("NB2 发送的消息含无法转换的消息段，已丢弃：{Types}", string.Join("、", dropped));
        if (body.Count == 0) return Failed("消息为空");

        SendMessageResult result = action == SEND_GROUP
            ? await api.SendGroupMessageAsync(parameters["group_id"]?.GetValue<long>() ?? 0, body, cancellationToken)
            : await api.SendFriendMessageAsync(parameters["user_id"]?.GetValue<long>() ?? 0, body, cancellationToken);
        if (!result.IsSuccess)
        {
            _logger.LogWarning("NB2 的消息发送失败：{Code} {Error}", result.Code, result.ErrorMessage);
            return Failed(result.ErrorMessage);
        }

        DateTimeOffset time = result.Time is { } sent ? new DateTimeOffset(sent) : _time.GetUtcNow();
        return Ok(
            new JsonObject
            {
                ["message_seq"] = (long)result.MessageId,
                ["time"]        = time.ToUnixTimeSeconds()
            });
    }

    private static MilkyCallResult Ok(JsonObject data) =>
        new()
        {
            StatusCode = OK_STATUS,
            Body = new JsonObject
            {
                ["status"]  = "ok",
                ["retcode"] = 0,
                ["data"]    = data
            }.ToJsonString()
        };

    private static MilkyCallResult Failed(string message) =>
        new()
        {
            StatusCode = OK_STATUS,
            Body = new JsonObject
            {
                ["status"]  = "failed",
                ["retcode"] = FAILED_CODE,
                ["message"] = message
            }.ToJsonString()
        };
}
