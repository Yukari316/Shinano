namespace Shinano.Plugin.NB2Bridge.Models;

/// <summary>NB2 进程内一个插件的统计，进程重启后清零。</summary>
public sealed class Nb2PluginStats
{
    /// <summary>到达该插件 matcher 的事件数（被禁用而忽略的不计）。</summary>
    public required long Events { get; init; }

    /// <summary>matcher 实际运行次数。</summary>
    public required long Runs { get; init; }

    /// <summary>运行出错次数。</summary>
    public required long Errors { get; init; }

    /// <summary>经 matcher 发起的平台 API 调用数。</summary>
    public required long ApiCalls { get; init; }

    /// <summary>最近一次错误；没有时为 null。</summary>
    public Nb2PluginError? LastError { get; init; }
}
