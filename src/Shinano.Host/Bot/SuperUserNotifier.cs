using Microsoft.Extensions.Logging;
using Shinano.Host.Notifications;
using Shinano.Host.Notifications.Models;
using Shinano.Infrastructure.Configuration;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;

namespace Shinano.Host.Bot;

/// <summary>
/// 以私聊向部署配置中的全部 Super User 发送宿主通知卡片。图片以 <c>base64://</c> 随消息发送，不依赖 RustFS；
/// 卡片无法绘制时改发文字。详细内容以日志为准。
/// </summary>
public sealed class SuperUserNotifier
{
    private const string BASE64_PREFIX = "base64://";

    private readonly BotAccessor                _bot;
    private readonly long[]                     _superUsers;
    private readonly TimeZoneInfo               _timeZone;
    private readonly NotificationCardRenderer?  _renderer;
    private readonly TimeProvider               _time;
    private readonly ILogger<SuperUserNotifier> _logger;

    /// <summary>创建通知器。</summary>
    /// <param name="bot">机器人连接。</param>
    /// <param name="deployment">部署配置，提供 Super User 与显示时间所用的时区。</param>
    /// <param name="renderer">卡片绘制器；为 null 时只发文字。</param>
    /// <param name="time">时间来源。</param>
    /// <param name="logger">日志。</param>
    public SuperUserNotifier(
        BotAccessor                bot,
        DeploymentOptions          deployment,
        NotificationCardRenderer?  renderer,
        TimeProvider               time,
        ILogger<SuperUserNotifier> logger)
    {
        _bot        = bot;
        _superUsers = deployment.SuperUsers;
        _timeZone   = TimeZoneInfo.TryFindSystemTimeZoneById(deployment.TimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Local;
        _renderer   = renderer;
        _time       = time;
        _logger     = logger;
    }

    /// <summary>在后台发送通知，不等待结果；未连接 LLBot 时只记录日志。发送失败记录错误，不重试。</summary>
    public void Notify(NotificationCard card)
    {
        IBotApi? api = _bot.Api;
        if (api is null)
        {
            _logger.LogWarning("未连接 LLBot，无法通知 Super User：{Text}", card.ToText());
            return;
        }

        _ = SendAsync(api, card);
    }

    private async Task SendAsync(IBotApi api, NotificationCard card)
    {
        MessageBody message = Compose(card);
        foreach (long user in _superUsers)
        {
            try
            {
                SendMessageResult result = await api.SendFriendMessageAsync(user, message);
                if (result.IsSuccess)
                    _logger.LogInformation("已通知 Super User {UserId}：{Title}", user, card.Title);
                else
                    _logger.LogError(
                        "通知 Super User {UserId} 失败：{Code} {Error}",
                        user,
                        result.Code,
                        result.ErrorMessage);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "通知 Super User {UserId} 失败", user);
            }
        }
    }

    private MessageBody Compose(NotificationCard card)
    {
        if (_renderer is null) return card.ToText();
        try
        {
            DateTimeOffset now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _timeZone);
            byte[] png = _renderer.Render(card, now);
            return new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制通知卡片失败，改发文字");
            return card.ToText();
        }
    }
}
