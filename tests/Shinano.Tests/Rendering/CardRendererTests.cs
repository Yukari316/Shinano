using Shinano.Framework.Rendering;
using Shinano.Framework.Rendering.Models;
using SkiaSharp;

namespace Shinano.Tests.Rendering;

/// <summary>公共字体、文字换行与消息图片绘制。不做像素比对，只核对尺寸与关键行为。</summary>
public sealed class CardRendererTests
{
    private const string FAMILY_EMOJI = "\U0001F468‍\U0001F469‍\U0001F467";

    [Fact]
    public void HostFonts_LoadFromResourceDirectory() => Assert.Null(TestRendering.Fonts.LoadError);

    [Fact]
    public void Wrap_BreaksAtWordsAndKeepsEmojiSequencesWhole()
    {
        TextPainter painter = TestRendering.Fonts.Painter;
        SKTypeface regular = TestRendering.Fonts.Regular;
        float width = painter.Measure("hello world", regular, 18) - 1;

        Assert.Equal(["hello", "world"], painter.Wrap("hello world", regular, 18, width));
        Assert.Equal(["第一行", "第二行"], painter.Wrap("第一行\n第二行", regular, 18, 1000));
        // ZWJ 组合是一个字素，换行不会把它拆开。
        Assert.All(painter.Wrap(string.Concat(Enumerable.Repeat(FAMILY_EMOJI, 20)), regular, 18, 60), line => Assert.Equal(0, line.Length % FAMILY_EMOJI.Length));
    }

    [Fact]
    public void Wrap_TruncatesWithEllipsis()
    {
        IReadOnlyList<string> lines = TestRendering.Fonts.Painter.Wrap(new string('长', 200), TestRendering.Fonts.Regular, 18, 200, maxLines: 2);

        Assert.Equal(2, lines.Count);
        Assert.EndsWith("…", lines[^1]);
    }

    [Fact]
    public void Measure_FallsBackFromLatinToChineseFont()
    {
        TextPainter painter = TestRendering.Fonts.Painter;

        // Space Grotesk 没有汉字，回退到 Noto Sans SC 后宽度与直接使用中文字体相同。
        Assert.Equal(painter.Measure("相似度", TestRendering.Fonts.Regular, 18), painter.Measure("相似度", TestRendering.Fonts.Latin, 18), 0.01f);
    }

    [Fact]
    public void RenderList_UsesConfiguredWidthAndGrowsWithItems()
    {
        using SKBitmap shortImage = Decode(TestRendering.Images.RenderList(List(items: 1)));
        using SKBitmap longImage = Decode(TestRendering.Images.RenderList(List(items: 30)));

        Assert.Equal(TestRendering.WIDTH, shortImage.Width);
        Assert.Equal(TestRendering.WIDTH, longImage.Width);
        Assert.True(longImage.Height > shortImage.Height * 4);
    }

    [Fact]
    public void RenderDocument_SeparateModeReturnsEachImageOnceInOrder()
    {
        byte[] first = Png(20, 10);
        byte[] second = Png(10, 20);
        RenderedDocument rendered = TestRendering.Images.RenderDocument(new DocumentCard
        {
            Markdown  = "![甲](a) 与 ![乙](b)，再看 ![甲](a) 和 ![坏图](broken) ![缺失](missing)",
            Images    = new Dictionary<string, byte[]> { ["a"] = first, ["b"] = second, ["broken"] = [1, 2, 3] },
            ImageMode = DocumentImageMode.Separate
        });

        Assert.Equal([first, second], rendered.Attachments);
        using SKBitmap image = Decode(rendered.Image);
        Assert.Equal(TestRendering.WIDTH, image.Width);
    }

    [Fact]
    public void RenderDocument_InlineModeDrawsImagesIntoDocument()
    {
        string markdown = "正文\n\n![图](a)\n\n结尾";
        DocumentCard withImage = new()
        {
            Markdown = markdown,
            Images   = new Dictionary<string, byte[]> { ["a"] = Png(400, 400) }
        };

        RenderedDocument rendered = TestRendering.Images.RenderDocument(withImage);
        RenderedDocument withoutImage = TestRendering.Images.RenderDocument(new DocumentCard { Markdown = markdown });

        Assert.Empty(rendered.Attachments);
        using SKBitmap large = Decode(rendered.Image);
        using SKBitmap small = Decode(withoutImage.Image);
        Assert.True(large.Height > small.Height + 300);
    }

    private static ListCard List(int items) =>
        new()
        {
            Title = "有人问",
            Page  = 1,
            Pages = 1,
            Items = [.. Enumerable.Range(1, items).Select(index => new ListCardItem
            {
                Index = index,
                Tag   = index % 2 == 0 ? "RG" : null,
                Text  = $"问题 {index} 😀"
            })],
            Hint = "发送 看看有人问 页码 翻页"
        };

    private static byte[] Png(int width, int height)
    {
        using SKBitmap bitmap = new(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKBitmap Decode(byte[] png) => SKBitmap.Decode(png);
}
