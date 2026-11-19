namespace Shinano.Plugin.NB2Bridge.Runtime;

/// <summary>一次启动 NB2 进程所用的参数。</summary>
public sealed class Nb2Launch
{
    /// <summary>NB2 的 HTTP 端口（控制 API 所在），只监听 127.0.0.1。</summary>
    public required int Port { get; init; }

    /// <summary>NB2Bridge 扮演 Milky 实现端的端口，只监听 127.0.0.1。</summary>
    public required int MilkyPort { get; init; }

    /// <summary>本次启动随机生成的 token，Milky 连接与控制 API 共用。</summary>
    public required string Token { get; init; }

    /// <summary>同步给 NB2 的 Super User。</summary>
    public required IReadOnlyList<long> SuperUsers { get; init; }

    /// <summary>要加载的插件模块。</summary>
    public required IReadOnlyList<string> Modules { get; init; }

    /// <summary>ffmpeg 所在目录；不可用时为 null，不修改 PATH。</summary>
    public string? FfmpegDirectory { get; init; }
}
