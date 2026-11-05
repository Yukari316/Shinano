namespace Shinano.Plugin.Hso.Models;

/// <summary>处理一张待审核图片的结果。</summary>
public enum HsoReviewOutcome
{
    /// <summary>已处理。</summary>
    Done,

    /// <summary>不在审核队列中（已被处理或图片已删除）。</summary>
    NotFound,

    /// <summary>原图下载失败，没有可覆盖的原图。</summary>
    NoCandidate,

    /// <summary>查询获取的原图与图库中的另一张图片相同，未覆盖。</summary>
    Duplicate
}
