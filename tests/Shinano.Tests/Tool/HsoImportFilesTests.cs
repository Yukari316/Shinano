using Shinano.Plugin.Hso;
using Shinano.Plugin.Hso.Models;
using Shinano.Tool.HsoImport;
using Shinano.Tool.HsoImport.Models;
using SkiaSharp;

namespace Shinano.Tests.Tool;

/// <summary><c>hso-import</c> 的格式识别与文件名解析，结果须与 Hso 插件相同。</summary>
public sealed class HsoImportFilesTests
{
    // 1×1 透明 GIF89a；SkiaSharp 不能编码 GIF。
    private static readonly byte[] TinyGif =
    [
        0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00, 0x01, 0x00, 0x80, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x00, 0x21, 0xF9, 0x04,
        0x01, 0x00, 0x00, 0x00, 0x00, 0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x02, 0x02, 0x44, 0x01, 0x00, 0x3B
    ];

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png", ".png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg", ".jpg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp", ".webp")]
    public void Detect_MatchesPluginForEncodedImages(SKEncodedImageFormat encoding, string mediaType, string extension) =>
        AssertSameFormat(Encode(encoding), mediaType, extension);

    [Fact]
    public void Detect_MatchesPluginForGif() => AssertSameFormat(TinyGif, "image/gif", ".gif");

    [Fact]
    public void Detect_RejectsNonImages()
    {
        byte[][] samples = ["PK\u0003\u0004 not an image"u8.ToArray(), "RIFF\0\0\0\0WAVEfmt "u8.ToArray(), [0xFF, 0xD8], []];
        foreach (byte[] sample in samples)
        {
            Assert.Null(GalleryFiles.Detect(sample));
            Assert.Null(HsoImageFiles.Detect(sample));
        }
    }

    [Theory]
    [InlineData("12345_p0.jpg", 12345L, 0)]
    [InlineData("sub/dir/98765432_p12.PNG", 98765432L, 12)]
    [InlineData("123456789012_p9999.webp", 123456789012L, 9999)]
    public void ParsePixivName_ReadsPidAndPage(string fileName, long pid, int page)
    {
        PixivPage? parsed = GalleryFiles.ParsePixivName(fileName);
        HsoPixivPage? plugin = HsoImageFiles.ParsePixivName(fileName);

        Assert.NotNull(parsed);
        Assert.NotNull(plugin);
        Assert.Equal((pid, page), (parsed.Pid, parsed.Page));
        Assert.Equal((plugin.Pid, plugin.Page), (parsed.Pid, parsed.Page));
    }

    [Theory]
    [InlineData("12345.jpg")]
    [InlineData("12345_p0")]
    [InlineData("abc_p0.jpg")]
    [InlineData("1234567890123_p0.jpg")]
    [InlineData("12345_p0.jpg.txt")]
    [InlineData("12345_p0/cover.jpg")]
    public void ParsePixivName_RejectsOtherNames(string fileName)
    {
        Assert.Null(GalleryFiles.ParsePixivName(fileName));
        Assert.Null(HsoImageFiles.ParsePixivName(fileName));
    }

    private static void AssertSameFormat(byte[] content, string mediaType, string extension)
    {
        ImageFormat? detected = GalleryFiles.Detect(content.AsSpan(0, Math.Min(content.Length, GalleryFiles.HEADER_LENGTH)));
        HsoImageFormat? plugin = HsoImageFiles.Detect(content);

        Assert.NotNull(detected);
        Assert.NotNull(plugin);
        Assert.Equal((mediaType, extension), (detected.MediaType, detected.Extension));
        Assert.Equal((plugin.MediaType, plugin.Extension), (detected.MediaType, detected.Extension));
    }

    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using SKBitmap bitmap = new(16, 16);
        bitmap.Erase(SKColors.Transparent);
        using SKData data = bitmap.Encode(format, 90);
        return data.ToArray();
    }
}
