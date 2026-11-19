namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 搜索结果指向的图站。声明顺序即取原图的优先顺序；<see cref="Konachan" /> 不经图源服务转发，只用于识别，只发卡片。
/// </summary>
public enum ImageSite
{
    /// <summary>Pixiv。</summary>
    Pixiv,

    /// <summary>Danbooru。</summary>
    Danbooru,

    /// <summary>Yande.re。</summary>
    Yandere,

    /// <summary>Gelbooru。</summary>
    Gelbooru,

    /// <summary>Sankaku Channel。</summary>
    Sankaku,

    /// <summary>e-shuushuu，全年龄站。</summary>
    Eshuushuu,

    /// <summary>Konachan。</summary>
    Konachan
}
