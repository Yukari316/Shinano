namespace Shinano.Infrastructure.Media;

/// <summary>一条持久媒体引用的归属。</summary>
public sealed class MediaReference
{
    /// <summary>持有引用的插件 ID。</summary>
    public required string PluginId { get; init; }

    /// <summary>插件内的业务键。</summary>
    public required string RefKey { get; init; }

    /// <summary>业务对象所属的机器人账号；与账号无关时为 null。</summary>
    public long? SelfId { get; init; }
}
