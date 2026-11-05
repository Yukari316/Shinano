using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shinano.Infrastructure.Hosting;

/// <summary>
/// 宿主与插件后台任务的唯一调度器。任务按固定间隔触发；所有者停用或调度暂停时跳过触发，
/// 正在执行的一次允许完成；上一次尚未结束时跳过本次触发。传给任务的取消令牌只在进程停止时取消。
/// </summary>
public sealed class HostScheduler : IHostedService, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, bool> _disabledOwners = new();
    private readonly ConcurrentBag<Task>                _loops          = [];
    private readonly CancellationTokenSource            _stopping       = new();
    private readonly ILogger<HostScheduler>             _logger;
    private readonly TimeProvider                       _time;
    private volatile bool                               _paused;

    /// <summary>创建调度器。</summary>
    public HostScheduler(ILogger<HostScheduler> logger, TimeProvider time)
    {
        _logger = logger;
        _time   = time;
    }

    /// <summary>
    /// 暂停全部触发，用于数据库离线等全局停止状态；正在执行的任务不受影响。
    /// </summary>
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    /// <summary>
    /// 登记一个周期任务并立即开始计时。首次触发在一个间隔之后，<paramref name="runImmediately" /> 为 true 时先执行一次。
    /// </summary>
    /// <param name="owner">所有者：宿主或插件 ID，用于整体启停。</param>
    /// <param name="name">任务名，用于日志。</param>
    /// <param name="interval">触发间隔，必须为正。</param>
    /// <param name="job">任务内容。异常只记录日志，不影响下次触发。</param>
    /// <param name="runImmediately">是否登记后立即执行一次。</param>
    public void Register(
        string                        owner,
        string                        name,
        TimeSpan                      interval,
        Func<CancellationToken, Task> job,
        bool                          runImmediately)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(interval, TimeSpan.Zero);
        // 不传递登记方的执行上下文：SqlSugarScope 以 AsyncLocal 为每个异步流程分配客户端，继承后任务会与登记方
        // （如插件 InitializeAsync）及同批登记的其他任务共用同一个数据库连接并发执行命令。
        using (ExecutionContext.SuppressFlow())
            _loops.Add(Task.Run(() => RunLoopAsync(owner, name, interval, job, runImmediately)));
    }

    /// <summary>启用或停用某个所有者的全部任务，从下一次触发生效。</summary>
    public void SetOwnerEnabled(string owner, bool enabled)
    {
        if (enabled) _disabledOwners.TryRemove(owner, out _);
        else _disabledOwners[owner] = true;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        await Task.WhenAll(_loops).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_stopping.IsCancellationRequested) await _stopping.CancelAsync();
        _stopping.Dispose();
    }

    private async Task RunLoopAsync(
        string                        owner,
        string                        name,
        TimeSpan                      interval,
        Func<CancellationToken, Task> job,
        bool                          runImmediately)
    {
        CancellationToken stopping = _stopping.Token;
        using PeriodicTimer timer = new(interval, _time);
        try
        {
            if (runImmediately) await RunOnceAsync(owner, name, job, stopping);
            while (await timer.WaitForNextTickAsync(stopping))
                await RunOnceAsync(owner, name, job, stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // 进程停止。
        }
    }

    // 在循环内顺序执行，执行期间到达的触发由 PeriodicTimer 合并为一次，即跳过重叠触发。
    private async Task RunOnceAsync(
        string                        owner,
        string                        name,
        Func<CancellationToken, Task> job,
        CancellationToken             stopping)
    {
        if (_paused || _disabledOwners.ContainsKey(owner)) return;

        try
        {
            await job(stopping);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "后台任务 {Owner}/{Job} 执行失败", owner, name);
        }
    }
}
