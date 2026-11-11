namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>NB2 进程内插件状态的页面视图（不含配置值）。</summary>
public sealed class Nb2RemoteView
{
    /// <summary>是否加载成功。</summary>
    public required bool Loaded { get; init; }

    /// <summary>显示名称。</summary>
    public required string Name { get; init; }

    /// <summary>描述。</summary>
    public required string Description { get; init; }

    /// <summary>matcher 数。</summary>
    public required int Matchers { get; init; }

    /// <summary>是否声明了配置模型。</summary>
    public required bool HasConfig { get; init; }

    /// <summary>统计。</summary>
    public required Nb2PluginStats Stats { get; init; }
}
