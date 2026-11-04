using System.Globalization;
using System.Security.Cryptography;
using SkiaSharp;

namespace Shinano.Plugin.ImageSources;

/// <summary>日志用的图片信息。腾讯按内容摘要拦截图片，记录 SHA256 便于对照。</summary>
public static class ImageInspector
{
    private const long KIB = 1024;
    private const long MIB = KIB * 1024;

    /// <summary>以 B、KB 或 MB 表示的大小（1 KB = 1024 B），如「512 B」「1.6 KB」「8.7 MB」；未知时为「未知」。</summary>
    public static string FormatSize(long? bytes) =>
        bytes switch
        {
            null      => "未知",
            < KIB     => string.Create(CultureInfo.InvariantCulture, $"{bytes} B"),
            < MIB     => string.Create(CultureInfo.InvariantCulture, $"{(double)bytes / KIB:0.#} KB"),
            { } large => string.Create(CultureInfo.InvariantCulture, $"{(double)large / MIB:0.#} MB")
        };

    /// <summary>内容的 SHA256（小写十六进制）。</summary>
    public static string Sha256(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

    /// <summary>编码格式与尺寸，如「Jpeg 1283×1492」；无法识别时为「无法识别」。只读取文件头，不解码像素。</summary>
    public static string Describe(byte[] content)
    {
        using SKData data = SKData.CreateCopy(content);
        using SKCodec? codec = SKCodec.Create(data);
        return codec is null ? "无法识别" : $"{codec.EncodedFormat} {codec.Info.Width}×{codec.Info.Height}";
    }
}
