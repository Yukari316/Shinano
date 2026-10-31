using Shinano.Framework.Plugins;
using Shinano.Plugin.Qa.Models;
using Shinano.Plugin.Qa.Options;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.Qa;

/// <summary>
/// 在普通事件链上回答群消息：只处理纯文本消息，去掉首尾空白后匹配本群词条，命中最新的一条后回答并终止事件链。
/// 指令先于普通事件处理，已被指令占用的消息不会到达这里。不设冷却。
/// </summary>
public sealed class QaResponder
{
    private readonly QaStore                      _store;
    private readonly QaMatcher                    _matcher;
    private readonly QaAnswerSender               _sender;
    private readonly IPluginInitializationContext _context;

    /// <summary>创建应答器。</summary>
    public QaResponder(QaStore store, QaMatcher matcher, QaAnswerSender sender, IPluginInitializationContext context)
    {
        _store   = store;
        _matcher = matcher;
        _sender  = sender;
        _context = context;
    }

    /// <summary>处理一条收到的消息。</summary>
    public async ValueTask OnMessageAsync(MessageReceivedEvent e)
    {
        if (e.Message.SourceType != MessageSourceType.Group) return;
        MessageBody body = e.Message.Body;
        if (body.Count == 0 || body.Any(segment => segment is not TextSegment)) return;

        string text = body.GetText().Trim();
        if (text.Length == 0) return;

        TimeSpan timeout = TimeSpan.FromMilliseconds(_context.Config<QaConfig>().Value.RegexTimeoutMilliseconds);
        if (_matcher.Match(await _store.GroupAsync(e.Message.GroupId), text, timeout) is not { } entry) return;

        e.IsContinueEventChain = false;
        await _sender.SendAsync(e.Api, e.Message.GroupId, entry.Answer);
    }
}
