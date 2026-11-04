namespace Shinano.Plugin.ImageSources.Models;

/// <summary>iqdb.org 的服务号（表单字段 <c>service[]</c> 的取值），只列出 <see cref="ImageSite" /> 中有的图站。</summary>
internal enum IqdbService
{
    /// <summary>Danbooru。</summary>
    Danbooru = 1,

    /// <summary>Konachan。</summary>
    Konachan = 2,

    /// <summary>Yande.re。</summary>
    Yandere = 3,

    /// <summary>Gelbooru。</summary>
    Gelbooru = 4,

    /// <summary>Sankaku Channel。</summary>
    Sankaku = 5,

    /// <summary>e-shuushuu。</summary>
    Eshuushuu = 6
}
