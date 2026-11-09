namespace Shinano.Plugin.Bilibili.Models;

/// <summary>视频投稿。</summary>
public sealed class BiliVideo
{
    /// <summary>BV 号。</summary>
    public required string Bvid { get; init; }

    /// <summary>封面原图地址（https）。</summary>
    public required string Cover { get; init; }

    /// <summary>标题。</summary>
    public required string Title { get; init; }

    /// <summary>简介，可能为空。</summary>
    public required string Description { get; init; }

    /// <summary>时长文字，如 <c>04:32</c>。</summary>
    public required string Duration { get; init; }
}
