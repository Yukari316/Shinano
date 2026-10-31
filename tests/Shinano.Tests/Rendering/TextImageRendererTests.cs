using Shinano.Framework.Rendering;
using Shinano.Framework.Rendering.Models;
using SkiaSharp;

namespace Shinano.Tests.Rendering;

/// <summary>公共字体、文字换行与长图绘制。不做像素比对，只核对尺寸与关键行为。</summary>
public sealed class TextImageRendererTests
{
    private const int    SCALED_WIDTH = 1440;
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
    public void RenderTextImage_GrowsWithRows()
    {
        TextImageDocument shortDocument = Document(rows: 1);
        TextImageDocument longDocument = Document(rows: 30);

        using SKBitmap shortImage = SKBitmap.Decode(TestRendering.Images.RenderTextImage(shortDocument));
        using SKBitmap longImage = SKBitmap.Decode(TestRendering.Images.RenderTextImage(longDocument));

        Assert.Equal(SCALED_WIDTH, shortImage.Width);
        Assert.Equal(SCALED_WIDTH, longImage.Width);
        Assert.True(longImage.Height > shortImage.Height * 5);
        using SKBitmap empty = SKBitmap.Decode(TestRendering.Images.RenderTextImage(new TextImageDocument { Title = "空" }));
        Assert.True(empty.Height > 0);
    }

    private static TextImageDocument Document(int rows) =>
        new()
        {
            Title    = "有人问",
            Subtitle = "第 1/1 页",
            Level    = TextImageLevel.Warning,
            Rows     = [.. Enumerable.Range(1, rows).Select(index => new TextImageRow
            {
                Index = index,
                Tag   = index % 2 == 0 ? "RG" : null,
                Text  = $"问题 {index} 😀"
            })],
            Footer = "发送 看看有人问 页码 翻页"
        };
}
