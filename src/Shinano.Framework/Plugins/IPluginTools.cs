namespace Shinano.Framework.Plugins;

/// <summary>
/// 宿主提供的外部程序。按宿主配置解析：自动下载到部署数据目录（默认）、系统 <c>PATH</c> 或指定目录。
/// 不计入宿主健康状态，不可用时只表现为使用它的插件报错。
/// </summary>
public interface IPluginTools
{
    /// <summary>
    /// 返回含 ffmpeg、ffprobe、ffplay 三个可执行文件的目录（绝对路径），三者均已确认可运行。
    /// 首次调用时解析，自动下载模式下可能下载约 200 MB；成功后缓存，失败不缓存，下次调用重新解析。
    /// </summary>
    /// <exception cref="InvalidOperationException">ffmpeg 不可用；消息为可操作的原因，详情已写入宿主日志。</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> 已取消。</exception>
    Task<string> FfmpegDirectoryAsync(CancellationToken cancellationToken);
}
