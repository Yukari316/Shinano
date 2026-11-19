namespace Shinano.Host.Plugins.Staging;

/// <summary>已暂存、等待下一次重启应用的插件包变更。</summary>
public sealed class PendingPluginChange
{
    /// <summary>变更内容。</summary>
    public required PluginChange Change { get; init; }

    /// <summary>暂存时间（UTC）。</summary>
    public required DateTimeOffset StagedAt { get; init; }

    /// <summary>上传的插件包大小（字节）；删除时为 0。</summary>
    public required long SizeBytes { get; init; }
}
