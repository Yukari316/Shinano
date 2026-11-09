namespace Shinano.Plugin.Bilibili.Models;

/// <summary>一条动态中卡片与推送需要的内容。</summary>
public sealed class BiliDynamic
{
    /// <summary>动态 ID（<c>id_str</c>）。</summary>
    public required string Id { get; init; }

    /// <summary>排版类型。</summary>
    public required BiliDynamicKind Kind { get; init; }

    /// <summary>作者 UID。</summary>
    public required long AuthorId { get; init; }

    /// <summary>作者昵称。</summary>
    public required string AuthorName { get; init; }

    /// <summary>作者头像地址；没有时为空。</summary>
    public required string AuthorFace { get; init; }

    /// <summary>发布时间。</summary>
    public required DateTimeOffset PublishedAt { get; init; }

    /// <summary>动作，如「投稿了视频」。</summary>
    public required string Action { get; init; }

    /// <summary>是否为置顶动态。</summary>
    public bool Pinned { get; init; }

    /// <summary>标题（图文与专栏可能有）；没有时为 null。</summary>
    public string? Title { get; init; }

    /// <summary>正文富文本；转发时为转发评论。</summary>
    public IReadOnlyList<BiliText> Text { get; init; } = [];

    /// <summary>图片，按动态中的顺序。</summary>
    public IReadOnlyList<BiliPicture> Pictures { get; init; } = [];

    /// <summary>视频投稿；不是视频时为 null。</summary>
    public BiliVideo? Video { get; init; }

    /// <summary>附加卡片（投票、预约、商品等）的一行说明，如「预约：新曲首播」；没有时为 null。</summary>
    public string? Attachment { get; init; }

    /// <summary>被转发的动态；只在 <see cref="BiliDynamicKind.Forward" /> 时有值，原动态已删除时为 null。</summary>
    public BiliDynamic? Original { get; init; }
}
