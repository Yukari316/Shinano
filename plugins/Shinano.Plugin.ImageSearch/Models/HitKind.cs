namespace Shinano.Plugin.ImageSearch.Models;

/// <summary>搜索结果的类别，决定卡片内容与是否取原图。</summary>
public enum HitKind
{
    /// <summary>插画等图片作品，按来源取原图。</summary>
    Illustration,

    /// <summary>番剧画面，只发番剧卡片（trace.moe 结果附预览图）。</summary>
    Anime
}
