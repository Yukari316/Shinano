namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>NB2 页面插件列表的一项：清单中的安装信息，NB2 运行时加上进程内的实时状态。</summary>
public sealed class Nb2PluginView
{
    /// <summary>PyPI 包名。</summary>
    public required string Package { get; init; }

    /// <summary>模块名。</summary>
    public required string Module { get; init; }

    /// <summary>安装的版本。</summary>
    public required string Version { get; init; }

    /// <summary>是否经上传的 wheel 安装。</summary>
    public required bool FromWheel { get; init; }

    /// <summary>是否启用事件响应。</summary>
    public required bool Enabled { get; init; }

    /// <summary>安装时间。</summary>
    public required DateTimeOffset InstalledAt { get; init; }

    /// <summary>NB2 进程内的状态；NB2 未运行或读取失败时为 null。</summary>
    public Nb2RemoteView? Remote { get; init; }
}
