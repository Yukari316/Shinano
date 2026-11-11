using System.Text.Json.Nodes;
using Shinano.Framework.Plugins.Models;
using Sora.Entities.Interfaces;

namespace Shinano.Framework.Plugins;

/// <summary>当前机器人连接。处理事件时优先使用事件参数的 <c>Api</c>。</summary>
public interface IBotAccessor
{
    /// <summary>当前连接的 API；未连接 LLBot 时为 null。</summary>
    IBotApi? Api { get; }

    /// <summary>当前机器人账号。</summary>
    long SelfId { get; }

    /// <summary>部署配置的 Super User 列表（QQ 号），切换机器人账号后不变。</summary>
    IReadOnlyList<long> SuperUsers { get; }

    /// <summary>
    /// 以宿主的 LLBot 配置直接调用一个 Milky API，原样返回 HTTP 状态码与响应正文。不经 Sora：经此发送的消息不会被宿主归档，
    /// 发送消息必须使用 <see cref="Api" />。供转发其他协议端平台动作的插件（如 NB2Bridge）使用；LLBot 凭据不交给插件。
    /// </summary>
    /// <param name="action">Milky API 名称，如 <c>get_login_info</c>。</param>
    /// <param name="parameters">请求参数 JSON。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="HttpRequestException">LLBot 不可达。</exception>
    /// <exception cref="OperationCanceledException">已取消或超时。</exception>
    Task<MilkyCallResult> CallMilkyAsync(string action, JsonObject parameters, CancellationToken cancellationToken);
}
