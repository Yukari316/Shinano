using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins.Capabilities;
using Shinano.Framework.Rendering.Models;
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
    private const string TIME_FORMAT   = "yyyy-MM-dd HH:mm:ss";
    private const string CAUSE_LABEL   = "原因";
    private const string IMPACT_LABEL  = "影响";

    private readonly BotAccessor                _bot;
    private readonly long[]                     _superUsers;
    private readonly TimeZoneInfo               _timeZone;
    private readonly IPluginImages              _images;
    private readonly TimeProvider               _time;
    private readonly ILogger<SuperUserNotifier> _logger;

    /// <summary>创建通知器。</summary>
    /// <param name="bot">机器人连接。</param>
    /// <param name="deployment">部署配置，提供 Super User 与显示时间所用的时区。</param>
    /// <param name="images">宿主长图绘制；字体不可用而绘制失败时改发文字。</param>
    /// <param name="time">时间来源。</param>
    /// <param name="logger">日志。</param>
    public SuperUserNotifier(
        BotAccessor                bot,
        DeploymentOptions          deployment,
        IPluginImages              images,
        TimeProvider               time,
        ILogger<SuperUserNotifier> logger)
    {
        _bot        = bot;
        _superUsers = deployment.SuperUsers;
        _timeZone   = TimeZoneInfo.TryFindSystemTimeZoneById(deployment.TimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Local;
        _images     = images;
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

    // 副标题只写时间，等级只由竖条颜色表示；正文只有原因与影响两行。
    private MessageBody Compose(NotificationCard card)
    {
        try
        {
            DateTimeOffset now = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _timeZone);
            byte[] png = _images.RenderTextImage(
                new TextImageDocument
                {
                    Title    = card.Title,
                    Subtitle = now.ToString(TIME_FORMAT),
                    Level = card.Level switch
                    {
                        NotificationLevel.Error   => TextImageLevel.Error,
                        NotificationLevel.Warning => TextImageLevel.Warning,
                        _                         => TextImageLevel.Normal
                    },
                    Rows =
                    [
                        .. card.Cause is { } cause ? (TextImageRow[]) [new TextImageRow { Label = CAUSE_LABEL, Text = cause }] : [],
                        new TextImageRow { Label = IMPACT_LABEL, Text = card.Impact }
                    ]
                });
            return new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制通知卡片失败，改发文字");
            return card.ToText();
        }
    }
}
