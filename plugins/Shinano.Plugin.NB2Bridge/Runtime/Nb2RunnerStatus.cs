using System.Text.Json.Serialization;

namespace Shinano.Plugin.NB2Bridge.Runtime;

/// <summary>NB2 进程的状态快照。</summary>
public sealed class Nb2RunnerStatus
{
    /// <summary>进程状态。</summary>
    public required Nb2ProcessState State { get; init; }

    /// <summary>进程 ID；未运行时为 null。</summary>
    public int? Pid { get; init; }

    /// <summary>本次启动时间；未运行时为 null。</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>事件通道是否已连接。</summary>
    public required bool Connected { get; init; }

    /// <summary>最近 1 分钟内的意外退出次数。</summary>
    public required int RecentCrashes { get; init; }

    /// <summary>上一次进程退出码；没有时为 null。</summary>
    public int? LastExitCode { get; init; }

    /// <summary>最近一次启动或运行失败的原因；没有时为 null。</summary>
    public string? LastError { get; init; }

    /// <summary>本次启动使用的 ffmpeg 目录；不可用或尚未启动过时为 null。</summary>
    public string? FfmpegDirectory { get; init; }

    /// <summary>ffmpeg 版本（如 <c>n8.1-…</c>）；不可用、读取失败或尚未启动过时为 null。</summary>
    public string? FfmpegVersion { get; init; }

    /// <summary>ffmpeg 不可用的原因；可用或尚未启动过时为 null。</summary>
    public string? FfmpegError { get; init; }

    /// <summary>本次启动传入的环境变量（显示值）；未启动过时为空。</summary>
    public required IReadOnlyList<Nb2EnvironmentVariable> Environment { get; init; }
}

/// <summary>NB2 进程的状态。</summary>
public enum Nb2ProcessState
{
    /// <summary>未运行。</summary>
    Stopped,

    /// <summary>进程已启动，尚未连上或尚未就绪（含断线重连期间）。</summary>
    Starting,

    /// <summary>已连上并推送禁用集合，正在交付事件。</summary>
    Running,

    /// <summary>正在停止。</summary>
    Stopping,

    /// <summary>1 分钟内意外退出次数超过上限，已停止自动重启，需要手动启动。</summary>
    Failed,

    /// <summary>没有 NB2 运行环境（未运行 nb2-setup）。</summary>
    Missing
}

/// <summary>传给 NB2 进程的一个环境变量。</summary>
public sealed class Nb2EnvironmentVariable
{
    /// <summary>变量名。</summary>
    public required string Name { get; init; }

    /// <summary>实际值，只用于启动进程；含 token，不序列化给页面。</summary>
    [JsonIgnore]
    public string Value { get; init; } = string.Empty;

    /// <summary>页面显示的值：token 显示为已设置，PATH 只显示前置的目录。</summary>
    public required string Display { get; init; }
}
