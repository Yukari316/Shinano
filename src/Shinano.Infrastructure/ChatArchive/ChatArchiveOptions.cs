namespace Shinano.Infrastructure.ChatArchive;

/// <summary>聊天记录配置，对应配置节 <c>Shinano:ChatArchive</c>。</summary>
public sealed class ChatArchiveOptions
{
    /// <summary>配置节路径。</summary>
    public const string SECTION_NAME = "Shinano:ChatArchive";

    /// <summary>聊天记录保留天数，默认 30；过期分区与随之过期的聊天媒体被清理。</summary>
    public int RetentionDays { get; set; } = 30;

    /// <summary>提前创建的未来分区天数，默认 3。</summary>
    public int PartitionDaysAhead { get; set; } = 3;
}
