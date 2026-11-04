namespace Shinano.Plugin.ImageSources.Models;

/// <summary>插件使用的 SauceNAO 索引库，数值为 SauceNAO 的 <c>index_id</c>。</summary>
public enum SauceNaoIndex
{
    /// <summary>Pixiv Images。</summary>
    Pixiv = 5,

    /// <summary>Danbooru。</summary>
    Danbooru = 9,

    /// <summary>Yande.re。</summary>
    Yandere = 12,

    /// <summary>番剧。</summary>
    Anime = 21,

    /// <summary>成人番剧，结果按 R-18 处理。</summary>
    HAnime = 22,

    /// <summary>Gelbooru。</summary>
    Gelbooru = 25,

    /// <summary>Sankaku Channel。</summary>
    Sankaku = 27
}
