namespace Shinano.Host.Tools.Models;

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
