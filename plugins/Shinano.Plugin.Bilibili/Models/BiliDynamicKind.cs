namespace Shinano.Plugin.Bilibili.Models;

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
