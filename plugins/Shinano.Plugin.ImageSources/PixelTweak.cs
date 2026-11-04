using SkiaSharp;

namespace Shinano.Plugin.ImageSources;

/// <summary>
/// 腾讯按内容摘要拦截部分图片，同一张图再发也会失败。改动一个随机像素并按原格式重新编码后，摘要随之改变，可以重发一次。
/// </summary>
public static class PixelTweak
{
    private const int QUALITY = 95;

    /// <summary>改动一个随机像素后按原格式重新编码；不支持的格式（如可能是动图的 GIF）或解码失败时返回 null。</summary>
    public static byte[]? Apply(byte[] image, string mediaType)
    {
        SKEncodedImageFormat? format = mediaType switch
        {
            "image/jpeg" => SKEncodedImageFormat.Jpeg,
            "image/png"  => SKEncodedImageFormat.Png,
            "image/webp" => SKEncodedImageFormat.Webp,
            _            => null
        };
        if (format is not { } encoded) return null;

        // 无法识别的内容 SKBitmap.Decode 会抛出异常，先由 SKCodec 识别。
        using SKData source = SKData.CreateCopy(image);
        using SKCodec? codec = SKCodec.Create(source);
        if (codec is null) return null;

        using SKBitmap? bitmap = SKBitmap.Decode(codec);
        if (bitmap is not { Width: > 0, Height: > 0 }) return null;

        int x = Random.Shared.Next(bitmap.Width);
        int y = Random.Shared.Next(bitmap.Height);
        SKColor color = bitmap.GetPixel(x, y);
        // 只翻转红色通道最低位，肉眼不可见。
        bitmap.SetPixel(x, y, color.WithRed((byte)(color.Red ^ 1)));
        using SKData? data = bitmap.Encode(encoded, QUALITY);
        return data?.ToArray();
    }
}
