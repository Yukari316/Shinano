using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Shinano.Tool.HsoImport.Models;

namespace Shinano.Tool.HsoImport;

/// <summary>
/// 图库文件的识别，与 Hso 插件的规则一致（由契约测试核对）：按文件头识别格式（不信任扩展名），
/// 按 Pixiv 的默认命名 <c>&lt;pid&gt;_p&lt;页号&gt;.&lt;扩展名&gt;</c> 解析作品号与页号。
/// </summary>
public static partial class GalleryFiles
{
    /// <summary>识别格式需要读取的文件头长度：WebP 为 <c>RIFF</c>、4 字节长度与 <c>WEBP</c>。</summary>
    public const int HEADER_LENGTH = 12;

    private const int WEBP_TAG_OFFSET = 8;

    private static readonly byte[] JpegSignature  = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] PngSignature   = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif87Signature = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89Signature = "GIF89a"u8.ToArray();
    private static readonly byte[] RiffSignature  = "RIFF"u8.ToArray();
    private static readonly byte[] WebpTag        = "WEBP"u8.ToArray();

    /// <summary>按文件头识别格式；不是 JPEG、PNG、GIF 或 WebP 时返回 null。只检查签名，不校验内容是否完整。</summary>
    public static ImageFormat? Detect(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(JpegSignature)) return Format("image/jpeg", ".jpg");
        if (header.StartsWith(PngSignature)) return Format("image/png", ".png");
        if (header.StartsWith(Gif87Signature) || header.StartsWith(Gif89Signature)) return Format("image/gif", ".gif");
        if (header.Length >= HEADER_LENGTH && header.StartsWith(RiffSignature) && header[WEBP_TAG_OFFSET..].StartsWith(WebpTag))
            return Format("image/webp", ".webp");
        return null;
    }

    /// <summary>从文件名（可含目录，只看文件名部分）解析 Pixiv 作品号与页号；不符合命名时返回 null。</summary>
    public static PixivPage? ParsePixivName(string fileName)
    {
        Match match = PixivName().Match(Path.GetFileName(fileName));
        return match.Success && long.TryParse(match.Groups["pid"].Value, out long pid) && int.TryParse(match.Groups["page"].Value, out int page)
            ? new PixivPage
            {
                Pid  = pid,
                Page = page
            }
            : null;
    }

    /// <summary>从流的当前位置读到末尾，计算 SHA256（小写十六进制）。</summary>
    public static async Task<string> Sha256Async(Stream content) =>
        Convert.ToHexStringLower(await SHA256.HashDataAsync(content));

    private static ImageFormat Format(string mediaType, string extension) =>
        new()
        {
            MediaType = mediaType,
            Extension = extension
        };

    [GeneratedRegex(@"^(?<pid>\d{1,12})_p(?<page>\d{1,4})\.[A-Za-z0-9]+$")]
    private static partial Regex PixivName();
}
