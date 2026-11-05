namespace Shinano.Plugin.Hso.Models;

/// <summary>审核中对比的一张图片：图库中的图片或查询获取的原图。</summary>
public sealed class HsoReviewImage
{
    /// <summary>MIME 类型。</summary>
    public required string MediaType { get; init; }

    /// <summary>大小，单位字节。</summary>
    public required long Size { get; init; }

    /// <summary>宽度，单位像素；读取失败时为 null。</summary>
    public int? Width { get; init; }

    /// <summary>高度，单位像素；读取失败时为 null。</summary>
    public int? Height { get; init; }

    /// <summary>内容的 SHA256（小写十六进制）。</summary>
    public required string Sha256 { get; init; }
}
