namespace Shinano.Plugin.ImageSearch.Models;

/// <summary>经图源服务转发的搜索服务。</summary>
public enum SearchEngine
{
    /// <summary>SauceNAO，以图搜图的首选，也收录番剧。</summary>
    SauceNao,

    /// <summary>IQDB，SauceNAO 没有可信结果时的兜底，只覆盖 booru 类图站。</summary>
    Iqdb,

    /// <summary>trace.moe，以动画截图搜番。</summary>
    TraceMoe
}
