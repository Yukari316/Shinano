namespace Shinano.Framework.Plugins.Capabilities;

/// <summary>
/// 后台任务登记。插件不得自建定时器；停用后宿主不再触发，正在执行的一次允许完成；
/// 上一次尚未结束时跳过本次触发。传入任务的取消令牌只在进程停止时取消。
/// </summary>
public interface IPluginScheduler
{
    /// <summary>登记一个周期任务。只能在 <see cref="IShinanoPlugin.InitializeAsync" /> 中调用。</summary>
    /// <param name="name">任务名，用于日志。</param>
    /// <param name="interval">触发间隔，必须为正。</param>
    /// <param name="job">任务内容。异常只记录日志。</param>
    /// <param name="runImmediately">是否在插件启用后立即执行一次。</param>
    void Register(string name, TimeSpan interval, Func<CancellationToken, Task> job, bool runImmediately);
}
