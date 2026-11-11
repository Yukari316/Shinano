using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.NB2Bridge.Models;
using Sora.Entities.Events;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>
/// 作为宿主普通事件订阅链中的一员，把配置选中的事件交付给 NB2 并等待消息处理的继续/终止：NB2 有 matcher 确实开始处理（全部运行前处理器
/// 已放行）时立即回报终止，不等处理结束；没有 matcher 开始处理、未连接、交付失败或等待超时都继续，超时后的回报只记警告日志，不重新交付。
/// </summary>
public sealed class Nb2EventRelay
{
    private readonly IPluginInitializationContext _context;
    private readonly Nb2Runner                    _runner;
    private readonly EventTracker                 _tracker;
    private readonly ILogger<Nb2EventRelay>       _logger;

    /// <summary>创建中继。</summary>
    public Nb2EventRelay(IPluginInitializationContext context, Nb2Runner runner, EventTracker tracker, ILogger<Nb2EventRelay> logger)
    {
        _context = context;
        _runner  = runner;
        _tracker = tracker;
        _logger  = logger;
    }

    /// <summary>处理一个宿主事件。</summary>
    public async ValueTask HandleAsync(BotEvent e)
    {
        if (MilkyEvents.Classify(e) is not { } kind) return;
        Nb2Config config = _context.Config<Nb2Config>().Value;
        bool wanted = kind switch
        {
            Nb2EventKind.GroupMessage   => config.DeliverGroupMessages,
            Nb2EventKind.PrivateMessage => config.DeliverPrivateMessages,
            Nb2EventKind.Notice         => config.DeliverNotices,
            _                           => config.DeliverRequests
        };
        if (!wanted || !_runner.IsReady || _runner.Server is not { } server) return;

        await DeliverAsync(server, e, TimeSpan.FromSeconds(config.EventTimeoutSeconds));
    }

    /// <summary>经指定的事件连接交付事件并等待结果；NB2 回报已处理时终止宿主事件链。</summary>
    public async Task DeliverAsync(MilkyServer server, BotEvent e, TimeSpan timeout)
    {
        string key = Guid.NewGuid().ToString("N");
        if (MilkyEvents.Build(e, key) is not { } payload) return;

        _tracker.Register(key);
        try
        {
            await server.SendAsync(payload, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _tracker.Forget(key);
            _logger.LogError(exception, "向 NB2 交付 {Type} 失败，事件链继续", Type(payload));
            return;
        }

        bool? handled = await _tracker.WaitAsync(key, timeout, CancellationToken.None);
        if (handled is null)
        {
            _logger.LogWarning(
                "NB2 未在 {Seconds} 秒内判定是否处理 {Type}（关联键 {Key}），事件链继续；NB2 之后的发送照常执行与归档",
                timeout.TotalSeconds,
                Type(payload),
                key);
            return;
        }

        if (handled.Value) e.IsContinueEventChain = false;
    }

    private static string? Type(JsonObject payload) => payload["event_type"]?.GetValue<string>();
}
