namespace Shinano.Infrastructure.ChatArchive;

/// <summary>
/// 在内存中跟踪归档缺口：每种原因同时最多一段进行中的缺口，重复开始不产生新区间；
/// 结束后的缺口等待写入数据库，写入失败时放回。线程安全。
/// </summary>
public sealed class ArchiveGapTracker
{
    private readonly Lock                                         _lock   = new();
    private readonly Dictionary<ArchiveGapCause, DateTimeOffset> _open   = new();
    private readonly List<ArchiveGap>                             _closed = [];

    /// <summary>开始一段缺口。</summary>
    /// <returns>是否新开始（此前没有该原因的进行中缺口）。</returns>
    public bool Open(ArchiveGapCause cause, DateTimeOffset at)
    {
        lock (_lock)
        {
            return _open.TryAdd(cause, at);
        }
    }

    /// <summary>结束一段缺口。</summary>
    /// <returns>是否确有进行中的缺口被结束。</returns>
    public bool Close(ArchiveGapCause cause, DateTimeOffset at)
    {
        lock (_lock)
        {
            return CloseLocked(cause, at);
        }
    }

    /// <summary>结束全部进行中的缺口，用于进程停止。</summary>
    public void CloseAll(DateTimeOffset at)
    {
        lock (_lock)
        {
            foreach (ArchiveGapCause cause in _open.Keys.ToArray())
                CloseLocked(cause, at);
        }
    }

    /// <summary>取出全部已结束、待写入的缺口。</summary>
    public IReadOnlyList<ArchiveGap> TakeClosed()
    {
        lock (_lock)
        {
            ArchiveGap[] closed = [.. _closed];
            _closed.Clear();
            return closed;
        }
    }

    /// <summary>写入失败时放回缺口，等待下次写入。</summary>
    public void Requeue(IEnumerable<ArchiveGap> gaps)
    {
        lock (_lock)
        {
            _closed.AddRange(gaps);
        }
    }

    private bool CloseLocked(ArchiveGapCause cause, DateTimeOffset at)
    {
        if (!_open.Remove(cause, out DateTimeOffset startedAt)) return false;

        _closed.Add(
            new ArchiveGap
            {
                Cause     = cause,
                StartedAt = startedAt,
                EndedAt   = at < startedAt ? startedAt : at
            });
        return true;
    }
}

/// <summary>一段已结束的归档缺口。</summary>
public sealed class ArchiveGap
{
    /// <summary>原因。</summary>
    public required ArchiveGapCause Cause { get; init; }

    /// <summary>开始时间，UTC。</summary>
    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>结束时间，UTC。</summary>
    public required DateTimeOffset EndedAt { get; init; }
}

/// <summary>归档缺口的原因，以 smallint 存储，取值不可更改。</summary>
public enum ArchiveGapCause : short
{
    /// <summary>数据库离线，消息无法入库。</summary>
    DatabaseOffline = 1,

    /// <summary>LLBot 断线，期间未上报的消息无法补回。</summary>
    BotDisconnected = 2,

    /// <summary>宿主归档缓冲已满，新记录被丢弃。</summary>
    Backlog = 3
}
