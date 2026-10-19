namespace Shinano.Infrastructure.ChatArchive;

/// <summary>聊天表的一个已存在分区。</summary>
public sealed class ChatPartition
{
    /// <summary>分区表名。</summary>
    public required string Name { get; init; }

    /// <summary>下界（含），UTC。</summary>
    public required DateTimeOffset From { get; init; }

    /// <summary>上界（不含），UTC。</summary>
    public required DateTimeOffset To { get; init; }
}
