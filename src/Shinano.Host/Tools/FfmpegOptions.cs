namespace Shinano.Host.Tools;

/// <summary>宿主 ffmpeg 服务的部署配置，位于 <c>Shinano:Ffmpeg</c>，重启生效。</summary>
public sealed class FfmpegOptions
{
    /// <summary>配置节名。</summary>
    public const string SECTION_NAME = "Shinano:Ffmpeg";

    /// <summary>ffmpeg 的来源。</summary>
    public FfmpegMode Mode { get; set; } = FfmpegMode.Download;

    /// <summary><see cref="FfmpegMode.Directory" /> 时含 ffmpeg、ffprobe、ffplay 的目录，相对路径基于部署数据目录。</summary>
    public string? Directory { get; set; }
}

/// <summary>ffmpeg 的来源。</summary>
public enum FfmpegMode
{
    /// <summary>自动下载宿主固定版本分支的静态构建到部署数据目录。</summary>
    Download,

    /// <summary>使用系统 <c>PATH</c> 中的 ffmpeg。</summary>
    SystemPath,

    /// <summary>使用配置指定的目录。</summary>
    Directory
}
