using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili.Feed;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Message;
using Sora.Entities.MessageWaiting;
using Sora.Entities.Segments;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.Bilibili.Login;

/// <summary>
/// Super User 私聊「B站登录」：二维码私聊发给触发者。已登录时先显示原账号并等待触发者回复 y/n（其他消息不消耗），
/// n 与超时明确回复已取消，y 时退出原账号再开始新登录；登录已失效时直接清空原账号的登录数据。
/// </summary>
[CommandGroup(Name = "bilibili")]
public sealed class BiliLoginCommands
{
    private const string YES           = "y";
    private const string NO            = "n";
    private const string BASE64_PREFIX = "base64://";

    private readonly IPluginInitializationContext _context;
    private readonly BiliSession                  _session;
    private readonly BiliLogin                    _login;
    private readonly BiliPoller                   _poller;
    private readonly ILogger<BiliLoginCommands>   _logger;

    /// <summary>创建指令组。</summary>
    public BiliLoginCommands(IPluginInitializationContext context, BiliSession session, BiliLogin login, BiliPoller poller, ILogger<BiliLoginCommands> logger)
    {
        _context = context;
        _session = session;
        _login   = login;
        _poller  = poller;
        _logger  = logger;
    }

    /// <summary>扫码登录 B站。</summary>
    [Command(
        Expressions = ["B站登录"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Friend,
        SuperUserOnly = true,
        Description = "扫码登录 B站")]
    public async ValueTask LoginAsync(MessageReceivedEvent e)
    {
        if (_session.Account is { } account)
        {
            // 登录已失效时不需要确认，直接清空原账号的登录数据。
            if (_session.State == BiliLoginState.LoggedIn && !await ConfirmLogoutAsync(e, account)) return;
            if (_session.State != BiliLoginState.LoggedIn) await _session.LogoutAsync(CancellationToken.None);
        }

        BiliQrCode qr;
        try
        {
            qr = await _login.StartAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is BiliApiException or HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(exception, "私聊生成 B站登录二维码失败");
            await e.ReplyAsync(new MessageBody(ReplyTexts.LOGIN_FAILED));
            return;
        }

        await e.ReplyAsync(new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(qr.Png)).AddText(ReplyTexts.QR_PROMPT));
        BiliQrState state = await _login.WaitAsync(qr.Key, CancellationToken.None);
        // 登录后立即刷新关注列表，否则要等每小时的刷新才开始按新账号的关注推送（面板登录由页面触发同样的刷新）。
        if (state == BiliQrState.Succeeded)
        {
            await _poller.RefreshFollowsAsync(CancellationToken.None);
            _poller.RequestPoll();
        }
        _logger.LogInformation("Super User {UserId} 私聊扫码登录 B站：{State}", e.Message.SenderId, state);
        await e.ReplyAsync(new MessageBody(state switch
        {
            BiliQrState.Succeeded => ReplyTexts.Format(ReplyTexts.LOGIN_SUCCEEDED, _session.Account?.Name ?? ""),
            BiliQrState.Expired   => ReplyTexts.QR_EXPIRED,
            _                     => ReplyTexts.LOGIN_FAILED
        }));
    }

    // 只有触发者回复 y 才退出原账号；n 与超时都明确回复已取消。
    private async Task<bool> ConfirmLogoutAsync(MessageReceivedEvent e, BiliAccount account)
    {
        await e.ReplyAsync(new MessageBody(ReplyTexts.Format(ReplyTexts.CONFIRM_LOGOUT, account.Name, account.Uid)));
        TimeSpan timeout = TimeSpan.FromSeconds(_context.Config<BiliConfig>().Value.ConfirmTimeoutSeconds);
        MessageReceivedEvent? reply = await e.WaitForNextMessageAsync(IsYesOrNo, timeout);
        if (reply is null)
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.LOGOUT_TIMEOUT));
            return false;
        }

        if (!string.Equals(reply.Message.Body.GetText().Trim(), YES, StringComparison.OrdinalIgnoreCase))
        {
            await e.ReplyAsync(new MessageBody(ReplyTexts.LOGOUT_DECLINED));
            return false;
        }

        _logger.LogInformation("Super User {UserId} 确认退出 B站账号 {Uid} 并重新登录", e.Message.SenderId, account.Uid);
        await _session.LogoutAsync(CancellationToken.None);
        return true;
    }

    private static bool IsYesOrNo(MessageReceivedEvent message)
    {
        MessageBody body = message.Message.Body;
        if (body.Count == 0 || body.Any(segment => segment is not TextSegment)) return false;
        string text = body.GetText().Trim();
        return string.Equals(text, YES, StringComparison.OrdinalIgnoreCase) || string.Equals(text, NO, StringComparison.OrdinalIgnoreCase);
    }
}
