namespace Shinano.Tool.Setup.Models;

/// <summary>
/// NB2 插件清单 <c>data/nb2/plugins.json</c> 的一项，与 NB2Bridge 插件的同名模型保持一致（契约测试核对）。
/// 工具只在首次创建清单时写入初始插件。
/// </summary>
public sealed class Nb2PluginEntry
{
    /// <summary>PyPI 包名。</summary>
    public required string Package { get; init; }

    /// <summary>NoneBot 加载的顶层模块名。</summary>
    public required string Module { get; init; }

    /// <summary>安装的版本。</summary>
    public required string Version { get; init; }

    /// <summary>上传安装时 <c>wheels/</c> 下的 wheel 文件名；从 PyPI 安装时为 null。</summary>
    public string? Wheel { get; init; }

    /// <summary>是否启用事件响应；新装插件默认不启用。</summary>
    public required bool Enabled { get; init; }

    /// <summary>安装时间（UTC）。</summary>
    public required DateTimeOffset InstalledAt { get; init; }
}
