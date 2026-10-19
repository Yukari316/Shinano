using Microsoft.Extensions.Logging;
using Shinano.Infrastructure.Configuration;
using Sora.Entities.Interfaces;

namespace Shinano.Host.Bot;

/// <summary>以私聊向部署配置中的全部 Super User 发送宿主通知。</summary>
public sealed class SuperUserNotifier
{
    private readonly BotAccessor                _bot;
    private readonly long[]                     _superUsers;
    private readonly ILogger<SuperUserNotifier> _logger;

    /// <summary>创建通知器。</summary>
    public SuperUserNotifier(BotAccessor bot, DeploymentOptions deployment, ILogger<SuperUserNotifier> logger)
    {
        _bot        = bot;
        _superUsers = deployment.SuperUsers;
        _logger     = logger;
    }

    /// <summary>在后台发送通知，不等待结果；未连接 LLBot 时只记录日志。发送失败记录错误，不重试。</summary>
    public void Notify(string text)
    {
        IBotApi? api = _bot.Api;
        if (api is null)
        {
            _logger.LogWarning("未连接 LLBot，无法通知 Super User：{Text}", text);
            return;
        }

        _ = SendAsync(api, text);
    }

    private async Task SendAsync(IBotApi api, string text)
    {
        foreach (long user in _superUsers)
        {
            try
            {
                await api.SendFriendMessageAsync(user, text);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "通知 Super User {UserId} 失败", user);
            }
        }
    }
}
