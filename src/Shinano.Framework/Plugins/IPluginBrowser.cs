namespace Shinano.Framework.Plugins;

/// <summary>
/// 宿主浏览器服务：供需要真实浏览器维护登录或风控状态的插件使用。浏览器为宿主下载的 Chrome for Testing（或宿主配置指定的路径），
/// 以无头模式运行，UA 与 Client Hints 覆盖为同版本正式版 Chrome 的值并去掉自动化标记。每个插件有独立的持久用户目录（视同机密），
/// 浏览器按需启动、会话结束即关闭。不计入宿主健康状态。
/// </summary>
public interface IPluginBrowser
{
    /// <summary>
    /// 启动浏览器并打开一个会话。同一插件的会话串行：前一个会话释放之前，后续调用等待。调用方必须释放会话以关闭浏览器进程。
    /// </summary>
    /// <exception cref="InvalidOperationException">浏览器无法下载、解析版本或启动；消息为可操作的原因，详情已写入宿主日志。</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken" /> 已取消。</exception>
    Task<IBrowserSession> OpenAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 删除插件的浏览器用户目录（Cookie、localStorage 等全部浏览器数据），如退出登录后。等待插件当前的会话结束后执行。
    /// </summary>
    Task ClearProfileAsync(CancellationToken cancellationToken);
}
