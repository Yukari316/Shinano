namespace Shinano.Plugin.Hso.Models;

/// <summary>作品信息的补全状态。只在图片被抽中发送时补全，不在导入时或后台查询。</summary>
public enum HsoInfoState
{
    /// <summary>尚未查询，或上次查询失败（下次抽中时再查）。</summary>
    Pending,

    /// <summary>已查到作品信息。</summary>
    Found,

    /// <summary>查询完成但没有可信结果，不再查询。</summary>
    NotFound
}
