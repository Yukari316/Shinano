using Shinano.Framework.Rendering;
using Shinano.Plugin.Hso;
using Shinano.Plugin.Hso.Gallery;
using Shinano.Plugin.Hso.Info;
using Shinano.Plugin.Hso.Persistence;
using Shinano.Plugin.ImageSources;
using SkiaSharp;

namespace Shinano.Tests.Plugins;

/// <summary>Hso 的发图冷却，以及图库的文件识别、作品信息、信息卡与缩略图。</summary>
public sealed class HsoTests
{
    // 发图冷却是插件 AGENTS.md 规定的业务常量，曾被误改为 1 秒。
    [Fact]
    public void DrawCooldown_Is20Seconds() => Assert.Equal(TimeSpan.FromSeconds(20), HsoCommands.Cooldown);

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
        Assert.Equal((16, 16), (detected.Width, detected.Height));
    }

    [Fact]
    public void SauceNaoInfo_PrefersPixivWorkFoundThroughBooruPost()
    {
        // 结果形状取自实测（数据为虚构）：最高结果是 Yande.re，来源只有图站帖子，标题字段是作品出处、作者是图站画师标签；
        // 帖子标注的来源是 Pixiv 作品。
        SearchHit yandere = new()
        {
            Engine     = SearchEngine.SauceNao,
            Similarity = 94.79,
            SourceName = "Yande.re",
            Kind       = HitKind.Illustration,
            Title      = "original",
            Author     = "booru_artist",
            Sources    = [new SourceRef { Site = ImageSite.Danbooru, Id = "1000001" }, new SourceRef { Site = ImageSite.Yandere, Id = "2000002" }]
        };
        ImageResolution pixiv = new()
        {
            Work     = new SourceRef { Site = ImageSite.Pixiv, Id = "100000003", Page = 0 },
            Title    = "作品标题",
            Author   = "作者名",
            AuthorId = 4000004,
            Tags     = [new WorkTag { Name = "オリジナル", Translation = "原创" }],
            Adult    = true
        };

        HsoWorkInfo info = HsoInfoResolver.InfoFromSauceNao(yandere, pixiv);

        Assert.Equal(("Pixiv", 100000003L, 0, "作品标题", "作者名", 4000004L), (info.SourceName, info.Pid, info.Page, info.Title, info.Author, info.AuthorId));
        Assert.Equal(["原创"], info.Tags);
        Assert.True(info.Nsfw);

        // 查不到 Pixiv 时用搜索结果中的信息，没有 pid。
        HsoWorkInfo booru = HsoInfoResolver.InfoFromSauceNao(yandere, new ImageResolution { Work = new SourceRef { Site = ImageSite.Yandere, Id = "2000002" } });
        Assert.Equal(("Yande.re", (long?)null, "original"), (booru.SourceName, booru.Pid, booru.Title));
    }

    [Fact]
    public void ManualInfo_CountsOnlyNonBlankFieldsAsInfo()
    {
        Assert.False(HsoReviews.HasAnyInfo(new HsoManualInfo { Title = "  ", Tags = [" "], Nsfw = true }));
        Assert.True(HsoReviews.HasAnyInfo(new HsoManualInfo { Tags = ["原创"] }));
        Assert.True(HsoReviews.HasAnyInfo(new HsoManualInfo { Pid = 1 }));
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
        Assert.Equal("夏の終わり\n作者：再音さく\nUID：11246082\nPID：127307417\nTAG：オリジナル 女の子", card.ToText());
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
