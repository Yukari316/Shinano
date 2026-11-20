using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Infrastructure.Hosting;
using Sora.Core.Types;
using Sora.Entities.Interfaces;
using Sora.Entities.Message;

namespace Shinano.Host.Bot;

/// <summary>
/// 以私聊向部署配置中的全部 Super User 发送宿主通知卡片。图片以 <c>base64://</c> 随消息发送，不依赖 RustFS；
/// 卡片无法绘制时改发文字。详细内容以日志为准。宿主与插件（经 <see cref="IPluginNotices" />）共用。
/// </summary>
public sealed class SuperUserNotifier : IPluginNotices
{
    private const string BASE64_PREFIX = "base64://";
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
    /// <param name="images">宿主消息图片；字体不可用而绘制失败时改发文字。</param>
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
        // 通知器在基础设施校验时区之前构造（含故障模式），时区无效时回退到本机时区，避免宿主无法启动。
        _timeZone   = TimeZoneInfo.TryFindSystemTimeZoneById(deployment.TimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Local;
        _images     = images;
        _time       = time;
        _logger     = logger;
    }

    /// <summary>在后台发送通知，不等待结果；未连接 LLBot 时只记录日志。发送失败记录错误，不重试。</summary>
    public void Notify(SuperUserNotice notice)
    {
        IBotApi? api = _bot.Api;
        if (api is null)
        {
            _logger.LogWarning("未连接 LLBot，无法通知 Super User：{Text}", ToText(notice));
            return;
        }

        _ = SendAsync(api, notice);
    }

    /// <inheritdoc />
    void IPluginNotices.NotifySuperUsers(SuperUserNotice notice) => Notify(notice);

    // 卡片无法绘制时代替图片发送的文字。
    private static string ToText(SuperUserNotice notice) =>
        string.Join(
            '\n',
            (string[]) [$"[Shinano] {notice.Title}", .. notice.Cause is null ? [] : (string[]) [$"原因：{notice.Cause}"], $"影响：{notice.Impact}"]);

    private async Task SendAsync(IBotApi api, SuperUserNotice notice)
    {
        MessageBody message = Compose(notice);
        foreach (long user in _superUsers)
        {
            try
            {
                SendMessageResult result = await api.SendFriendMessageAsync(user, message);
                if (result.IsSuccess)
                    _logger.LogInformation("已通知 Super User {UserId}：{Title}", user, notice.Title);
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

    // 标题块右上只写时间，等级只由状态标与标签颜色表示；正文只有原因与影响两行。
    private MessageBody Compose(SuperUserNotice notice)
    {
        try
        {
            byte[] png = _images.RenderNotice(
                new NoticeCard
                {
                    Title = notice.Title,
                    Code  = notice.Code,
                    Time  = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _timeZone),
                    Level = notice.Level,
                    Rows =
                    [
                        .. notice.Cause is { } cause ? (CardRow[]) [new CardRow { Label = CAUSE_LABEL, Text = cause }] : [],
                        new CardRow { Label = IMPACT_LABEL, Text = notice.Impact }
                    ]
                });
            return new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制通知卡片失败，改发文字");
            return ToText(notice);
        }
    }
}
