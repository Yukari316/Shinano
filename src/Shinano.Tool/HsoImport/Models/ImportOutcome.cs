namespace Shinano.Tool.HsoImport.Models;

/// <summary>一个文件的导入结果。</summary>
public enum ImportOutcome
{
    /// <summary>已上传并写入图库表。</summary>
    Imported,

    /// <summary>图库中已有相同内容（SHA256 相同）的图片。</summary>
    Duplicate,

    /// <summary>不是 JPEG、PNG、GIF 或 WebP。</summary>
    Unsupported,

    /// <summary>读取、上传或写入失败。</summary>
    Failed
}
