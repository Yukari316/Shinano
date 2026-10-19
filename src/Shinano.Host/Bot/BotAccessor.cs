using Shinano.Framework.Plugins;
using Sora.Entities.Interfaces;

namespace Shinano.Host.Bot;

/// <summary>当前机器人连接，由 <see cref="BotService" /> 在连接与断线时更新。</summary>
public sealed class BotAccessor : IBotAccessor
{
    private volatile IBotApi? _api;
    private long              _selfId;

    /// <inheritdoc />
    public IBotApi? Api => _api;

    /// <inheritdoc />
    public long SelfId => Interlocked.Read(ref _selfId);

    /// <summary>记录已连接的账号与 API。</summary>
    internal void SetConnected(IBotApi api, long selfId)
    {
        Interlocked.Exchange(ref _selfId, selfId);
        _api = api;
    }

    /// <summary>记录断线；保留账号以便断线期间仍能访问账号数据。</summary>
    internal void SetDisconnected() => _api = null;
}
