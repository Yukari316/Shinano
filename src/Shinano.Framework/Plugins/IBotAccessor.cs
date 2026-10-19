using Sora.Entities.Interfaces;

namespace Shinano.Framework.Plugins;

/// <summary>当前机器人连接。处理事件时优先使用事件参数的 <c>Api</c>。</summary>
public interface IBotAccessor
{
    /// <summary>当前连接的 API；未连接 LLBot 时为 null。</summary>
    IBotApi? Api { get; }

    /// <summary>当前机器人账号。</summary>
    long SelfId { get; }
}
