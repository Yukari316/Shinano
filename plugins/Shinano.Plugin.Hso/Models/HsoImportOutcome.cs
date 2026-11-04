namespace Shinano.Plugin.Hso.Models;

/// <summary>导入一张图片的结果。</summary>
public enum HsoImportOutcome
{
    /// <summary>已导入。</summary>
    Imported,

    /// <summary>图库中已有相同内容的图片，跳过。</summary>
    Duplicate,

    /// <summary>不是 JPEG、PNG、GIF 或 WebP 图片。</summary>
    Unsupported,

    /// <summary>写入失败，如超过部署的单文件上限。</summary>
    Failed
}
