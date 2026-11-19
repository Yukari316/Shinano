namespace Shinano.Plugin.Hso.Gallery;

/// <summary>面板导入中一个文件的结果。</summary>
public sealed class HsoImportResult
{
    /// <summary>文件名（zip 中为条目路径）；<c>AD</c> 导入为 null。</summary>
    public string? FileName { get; init; }

    /// <summary>结果。</summary>
    public required HsoImportOutcome Outcome { get; init; }

    /// <summary>导入后的图片 ID；未导入时为 null。</summary>
    public long? Id { get; init; }

    /// <summary>创建未导入的结果。</summary>
    public static HsoImportResult Of(string? fileName, HsoImportOutcome outcome) =>
        new()
        {
            FileName = fileName,
            Outcome  = outcome
        };
}

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
