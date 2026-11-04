namespace Shinano.Plugin.Hso.Models;

/// <summary>一次作品信息补全的结果类别。</summary>
public enum HsoLookupStatus
{
    /// <summary>查到了作品信息。</summary>
    Found,

    /// <summary>查询完成但没有可信结果。</summary>
    NotFound,

    /// <summary>图源服务未配置、出错或额度用完，下次再查。</summary>
    Failed
}
