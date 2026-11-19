using System.Collections.Concurrent;

namespace Shinano.Plugin.NB2Bridge.Milky;

/// <summary>等待 NB2 回报已交付事件的处理结果（终止为 true，继续为 false），按关联键配对。线程安全。</summary>
public sealed class EventTracker
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> _pending = new();

    /// <summary>登记一个即将交付的事件。必须在交付前登记，避免回报先于登记到达。</summary>
    public void Register(string key) =>
        _pending[key] = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>记录回报；未登记或已超时的键忽略。</summary>
    /// <returns>是否有对应的等待。</returns>
    public bool Complete(string key, bool handled) => _pending.TryRemove(key, out TaskCompletionSource<bool>? waiter) && waiter.TrySetResult(handled);

    /// <summary>放弃等待（交付失败时）。</summary>
    public void Forget(string key) => _pending.TryRemove(key, out _);

    /// <summary>等待回报。</summary>
    /// <returns>NB2 报告的结果；超时为 null。</returns>
    public async Task<bool?> WaitAsync(string key, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!_pending.TryGetValue(key, out TaskCompletionSource<bool>? waiter)) return null;
        try
        {
            return await waiter.Task.WaitAsync(timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            _pending.TryRemove(key, out _);
            return null;
        }
    }
}
