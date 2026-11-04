using SkiaSharp;

namespace Shinano.Plugin.Hso;

/// <summary>
/// 面板网格的缩略图：每次请求时即时绘制，不保存（存储空间有限，CPU 充足）。按长边缩小为 JPEG，动图取第一帧；
/// 解码时先让编解码器按比例缩小，减少大图的内存占用。
/// </summary>
public static class HsoThumbnails
{
    /// <summary>缩略图长边，单位像素。</summary>
    public const int LONG_SIDE = 480;

    private const int QUALITY = 80;

    /// <summary>绘制缩略图；无法解码时返回 null。</summary>
    public static byte[]? Render(byte[] content)
    {
        using SKData data = SKData.CreateCopy(content);
        using SKCodec? codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;

        float scale = Math.Min(1f, (float)LONG_SIDE / Math.Max(codec.Info.Width, codec.Info.Height));
        SKSizeI decoded = codec.GetScaledDimensions(scale);
        SKImageInfo decodeInfo = codec.Info.WithSize(decoded.Width, decoded.Height).WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul);
        using SKBitmap? source = SKBitmap.Decode(codec, decodeInfo);
        if (source is null) return null;

        int width = Math.Max(1, (int)Math.Round(codec.Info.Width * scale));
        int height = Math.Max(1, (int)Math.Round(codec.Info.Height * scale));
        // JPEG 没有透明通道，透明区域铺白底，避免变成黑色。
        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.White);
        using (SKImage image = SKImage.FromBitmap(source))
            surface.Canvas.DrawImage(image, new SKRect(0, 0, width, height), new SKSamplingOptions(SKCubicResampler.Mitchell));
        using SKImage snapshot = surface.Snapshot();
        using SKData? jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, QUALITY);
        return jpeg?.ToArray();
    }
}
