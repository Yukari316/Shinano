namespace Shinano.Tool.HsoImport.Models;

/// <summary>要写入图库的一张图片；作品信息留待插件在图片被抽中时补全。</summary>
public sealed class GalleryImage
{
    /// <summary>插件 bucket 中的对象 key。</summary>
    public required string ObjectKey { get; init; }

    /// <summary>内容的 SHA256（小写十六进制）。</summary>
    public required string Sha256 { get; init; }

    /// <summary>MIME 类型。</summary>
    public required string MediaType { get; init; }

    /// <summary>大小，单位字节。</summary>
    public required long Size { get; init; }

    /// <summary>相对导入目录的路径，以 <c>/</c> 分隔。</summary>
    public required string FileName { get; init; }

    /// <summary>Pixiv 作品号；文件名不符合 Pixiv 命名时为 null。</summary>
    public required long? Pid { get; init; }

    /// <summary>Pixiv 页号；文件名不符合 Pixiv 命名时为 null。</summary>
    public required int? Page { get; init; }
}
