using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.Hso;
using Shinano.Plugin.Hso.Entities;
using Shinano.Plugin.Hso.Models;
using SkiaSharp;

namespace Shinano.Tests.Plugins;

/// <summary>Hso 图库的文件识别、信息卡与缩略图。</summary>
public sealed class HsoTests
{
    [Theory]
    [InlineData("127307417_p0.jpg", 127307417L, 0)]
    [InlineData("zip/目录/98765_p12.png", 98765L, 12)]
    public void ParsePixivName_ReadsPidAndPage(string fileName, long pid, int page)
    {
        HsoPixivPage? parsed = HsoImageFiles.ParsePixivName(fileName);

        Assert.NotNull(parsed);
        Assert.Equal(pid, parsed.Pid);
        Assert.Equal(page, parsed.Page);
    }

    [Theory]
    [InlineData("cover.jpg")]
    [InlineData("127307417.jpg")]
    [InlineData("127307417_p0_master1200.jpg")]
    [InlineData("127307417_p0")]
    public void ParsePixivName_RejectsOtherNames(string fileName) => Assert.Null(HsoImageFiles.ParsePixivName(fileName));

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png", ".png")]
    [InlineData(SKEncodedImageFormat.Jpeg, "image/jpeg", ".jpg")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp", ".webp")]
    public void Detect_UsesFileHeader(SKEncodedImageFormat format, string mediaType, string extension)
    {
        HsoImageFormat? detected = HsoImageFiles.Detect(Encode(16, 16, format));

        Assert.NotNull(detected);
        Assert.Equal(mediaType, detected.MediaType);
        Assert.Equal(extension, detected.Extension);
    }

    [Fact]
    public void Detect_RejectsNonImages() => Assert.Null(HsoImageFiles.Detect("PK\u0003\u0004 not an image"u8.ToArray()));

    [Fact]
    public void Card_IsOmittedWithoutAnyWorkInfo() => Assert.Null(HsoSender.Card(new HsoImageRecord()));

    [Fact]
    public void Card_ShowsPixivWork()
    {
        HsoImageRecord record = new()
        {
            Pid      = 127307417,
            Page     = 2,
            Title    = "夏の終わり",
            Author   = "再音さく",
            AuthorId = 11246082,
            TagList  = ["オリジナル", "女の子"]
        };

        InfoCard? card = HsoSender.Card(record);

        Assert.Equal("オリジナル\n女の子", record.Tags);
        Assert.NotNull(card);
        Assert.Equal("夏の終わり", card.Title);
        Assert.Equal("PIXIV", card.Meta);
        Assert.Equal(["作者", "UID", "PID", "TAG"], card.Rows.Select(row => row.Label));
        CardRow tags = card.Rows[^1];
        Assert.Equal((CardRowStyle.Items, true), (tags.Style, tags.ItemBorder));
        Assert.Equal(["オリジナル", "女の子"], tags.Items);
        Assert.Equal("夏の終わり\n作者：再音さく\nUID：11246082\nPID：127307417\nTAG：オリジナル 女の子", HsoSender.ToText(card));
    }

    [Fact]
    public void Thumbnail_ShrinksToLongSideAsJpeg()
    {
        byte[]? thumbnail = HsoThumbnails.Render(Encode(2000, 1000, SKEncodedImageFormat.Png));

        Assert.NotNull(thumbnail);
        using SKCodec codec = SKCodec.Create(new SKMemoryStream(thumbnail));
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(HsoThumbnails.LONG_SIDE, codec.Info.Width);
        Assert.Equal(HsoThumbnails.LONG_SIDE / 2, codec.Info.Height);
        Assert.Null(HsoThumbnails.Render([1, 2, 3]));
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using SKBitmap bitmap = new(width, height);
        bitmap.Erase(SKColors.Transparent);
        using SKData data = bitmap.Encode(format, 90);
        return data.ToArray();
    }
}
