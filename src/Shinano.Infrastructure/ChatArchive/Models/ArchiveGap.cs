namespace Shinano.Infrastructure.ChatArchive.Models;

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
