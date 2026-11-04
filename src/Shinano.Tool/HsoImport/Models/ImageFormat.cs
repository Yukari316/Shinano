namespace Shinano.Tool.HsoImport.Models;

/// <summary>按文件头识别出的图片格式。</summary>
public sealed class ImageFormat
{
    /// <summary>MIME 类型，如 <c>image/png</c>。</summary>
    public required string MediaType { get; init; }

    /// <summary>对象 key 使用的扩展名，含点，如 <c>.png</c>。</summary>
    public required string Extension { get; init; }
}
