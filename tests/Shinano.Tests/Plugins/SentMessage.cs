namespace Shinano.Tests.Plugins;

/// <summary><see cref="FakeMilkyServer" /> 收到的一次发送请求。</summary>
public sealed class SentMessage
{
    /// <summary>是否为群消息。</summary>
    public required bool IsGroup { get; init; }

    /// <summary>群号或好友 ID。</summary>
    public required long TargetId { get; init; }

    /// <summary>全部文本段拼接的文本。</summary>
    public required string Text { get; init; }

    /// <summary>消息段数量。</summary>
    public required int SegmentCount { get; init; }

    /// <summary>原始请求 JSON。</summary>
    public required string Raw { get; init; }
}
