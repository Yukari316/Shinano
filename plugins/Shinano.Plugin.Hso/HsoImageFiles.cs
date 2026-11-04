using System.Text.RegularExpressions;
using Shinano.Plugin.Hso.Models;
using SkiaSharp;

namespace Shinano.Plugin.Hso;

/// <summary>
/// 图库文件的识别：按文件头识别格式（不信任扩展名），按 Pixiv 的默认命名 <c>&lt;pid&gt;_p&lt;页号&gt;.&lt;扩展名&gt;</c> 解析作品号与页号。
/// <c>Shinano.Tool hso-import</c> 按同一规则解析文件名。
/// </summary>
public static partial class HsoImageFiles
{
    /// <summary>识别图片格式；不是 JPEG、PNG、GIF 或 WebP 时返回 null。只读取文件头，不解码像素。</summary>
    public static HsoImageFormat? Detect(byte[] content)
    {
        using SKData data = SKData.CreateCopy(content);
        using SKCodec? codec = SKCodec.Create(data);
        return codec?.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => Format("image/jpeg", ".jpg"),
            SKEncodedImageFormat.Png  => Format("image/png", ".png"),
            SKEncodedImageFormat.Gif  => Format("image/gif", ".gif"),
            SKEncodedImageFormat.Webp => Format("image/webp", ".webp"),
            _                         => null
        };
    }

    /// <summary>从文件名（可含目录）解析 Pixiv 作品号与页号；不符合命名时返回 null。</summary>
    public static HsoPixivPage? ParsePixivName(string fileName)
    {
        Match match = PixivName().Match(Path.GetFileName(fileName));
        return match.Success && long.TryParse(match.Groups["pid"].Value, out long pid) && int.TryParse(match.Groups["page"].Value, out int page)
            ? new HsoPixivPage
            {
                Pid  = pid,
                Page = page
            }
            : null;
    }

    private static HsoImageFormat Format(string mediaType, string extension) =>
        new()
        {
            MediaType = mediaType,
            Extension = extension
        };

    [GeneratedRegex(@"^(?<pid>\d{1,12})_p(?<page>\d{1,4})\.[A-Za-z0-9]+$")]
    private static partial Regex PixivName();
}
