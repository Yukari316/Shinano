using Shinano.Framework.Plugins.Capabilities;
using Shinano.Infrastructure.Scheduling;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>
/// 收集插件在初始化期间登记的后台任务，插件首次启用后交给宿主调度器，使"立即执行一次"发生在启用之后。
/// </summary>
internal sealed class PluginScheduler : IPluginScheduler
{
    private readonly string        _pluginId;
    private readonly HostScheduler _scheduler;
    private readonly List<Action>  _pending = [];
    private          bool          _activated;

    /// <summary>创建登记入口。</summary>
    public PluginScheduler(string pluginId, HostScheduler scheduler)
    {
        _pluginId  = pluginId;
        _scheduler = scheduler;
    }

    /// <inheritdoc />
    public void Register(string name, TimeSpan interval, Func<CancellationToken, Task> job, bool runImmediately)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        if (_activated) throw new InvalidOperationException("后台任务只能在插件初始化期间登记");
        _pending.Add(() => _scheduler.Register(_pluginId, name, interval, job, runImmediately));
    }

    /// <summary>把登记的任务交给宿主调度器。只在首次启用时调用一次。</summary>
    public void Activate()
    {
        _activated = true;
        foreach (Action register in _pending)
            register();
        _pending.Clear();
    }
}
