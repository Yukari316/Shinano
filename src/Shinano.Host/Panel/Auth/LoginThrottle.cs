using System.Net;

namespace Shinano.Host.Panel.Auth;

/// <summary>
/// 登录失败按来源地址限速：同一地址在窗口期内失败达到上限后拒绝登录，直到窗口结束；登录成功清除该地址的记录。
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>窗口期内允许的失败次数。</summary>
    public const int MAX_FAILURES = 5;

    /// <summary>失败计数窗口。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly Lock                                 _lock     = new();
    private readonly Dictionary<IPAddress, FailureWindow> _failures = new();
    private readonly TimeProvider                         _time;

    /// <summary>创建限速器。</summary>
    public LoginThrottle(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>该地址当前是否被拒绝登录。</summary>
    public bool IsBlocked(IPAddress address)
    {
        lock (_lock)
        {
            return Current(address) is { Count: >= MAX_FAILURES };
        }
    }

    /// <summary>记录一次失败。</summary>
    public void RecordFailure(IPAddress address)
    {
        lock (_lock)
        {
            DateTimeOffset now = _time.GetUtcNow();
            foreach (IPAddress expired in _failures.Where(item => now - item.Value.StartedAt >= Window).Select(item => item.Key).ToArray())
                _failures.Remove(expired);

            FailureWindow window = Current(address) ?? new FailureWindow { StartedAt = now };
            window.Count++;
            _failures[address] = window;
        }
    }

    /// <summary>登录成功后清除记录。</summary>
    public void Reset(IPAddress address)
    {
        lock (_lock)
        {
            _failures.Remove(address);
        }
    }

    // 调用方持有锁；过期窗口在读取时移除，记录失败时清理全部过期记录。
    private FailureWindow? Current(IPAddress address)
    {
        if (!_failures.TryGetValue(address, out FailureWindow? window)) return null;
        if (_time.GetUtcNow() - window.StartedAt < Window) return window;

        _failures.Remove(address);
        return null;
    }

    private sealed class FailureWindow
    {
        public required DateTimeOffset StartedAt { get; init; }

        public int Count { get; set; }
    }
}
