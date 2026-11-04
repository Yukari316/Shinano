namespace Shinano.Plugin.Hso.Models;

/// <summary><c>AD</c> 导入的结果。</summary>
public enum HsoPixivImportOutcome
{
    /// <summary>已导入。</summary>
    Imported,

    /// <summary>该作品页或相同内容的图片已在图库中。</summary>
    Exists,

    /// <summary>作品不存在、已删除或页号越界。</summary>
    NotFound,

    /// <summary>图源服务未配置、查询或下载失败，或写入失败。</summary>
    Failed
}
