namespace Shinano.Plugin.NB2Bridge.Models;

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
