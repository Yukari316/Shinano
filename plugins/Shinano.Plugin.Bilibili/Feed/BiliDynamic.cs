namespace Shinano.Plugin.Bilibili.Feed;

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

/// <summary>动态的排版类型。</summary>
public enum BiliDynamicKind
{
    /// <summary>图文与纯文字（<c>DYNAMIC_TYPE_DRAW</c>、<c>DYNAMIC_TYPE_WORD</c>）。</summary>
    Opus,

    /// <summary>专栏（<c>DYNAMIC_TYPE_ARTICLE</c>），只有摘要。</summary>
    Article,

    /// <summary>视频投稿（<c>DYNAMIC_TYPE_AV</c>）。</summary>
    Video,

    /// <summary>转发（<c>DYNAMIC_TYPE_FORWARD</c>），被转发的动态在 <see cref="BiliDynamic.Original" />。</summary>
    Forward,

    /// <summary>直播推荐（开播时自动产生），不推送。</summary>
    LiveRecommend,

    /// <summary>其他类型，用通用排版。</summary>
    Generic
}

/// <summary>富文本的一个片段。</summary>
public sealed class BiliText
{
    /// <summary>显示方式。</summary>
    public required BiliTextKind Kind { get; init; }

    /// <summary>文字。</summary>
    public required string Text { get; init; }
}

/// <summary>富文本片段的显示方式。</summary>
public enum BiliTextKind
{
    /// <summary>普通文字。</summary>
    Plain,

    /// <summary>高亮文字：@、话题、链接、视频号等可点击内容，卡片中不附链接，只改颜色。</summary>
    Highlight,

    /// <summary>B站表情（如 <c>[doge]</c>），显示其文字。</summary>
    Emoji
}

/// <summary>动态中的一张图片。</summary>
public sealed class BiliPicture
{
    /// <summary>原图地址（https）。</summary>
    public required string Url { get; init; }

    /// <summary>宽度，单位像素；未知时为 0。</summary>
    public required int Width { get; init; }

    /// <summary>高度，单位像素；未知时为 0。</summary>
    public required int Height { get; init; }
}

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
