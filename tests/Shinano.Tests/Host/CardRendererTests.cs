using Shinano.Framework.Rendering;
using Shinano.Host.Bot;
using Shinano.Host.Rendering;
using SkiaSharp;

namespace Shinano.Tests.Host;

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

        // Saira Expanded 没有汉字，回退到 Noto Sans SC 后宽度与直接使用中文字体相同。
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
    public void RenderStatus_MovesOverflowingDetailToItsOwnLines()
    {
        using SKBitmap fits = Decode(TestRendering.Images.RenderStatus(Status("nb2ping", "v2.7.2")));
        using SKBitmap overflows = Decode(TestRendering.Images.RenderStatus(Status("链接分享自动解析插件（视频、图文与直播）", string.Join('/', Enumerable.Repeat("very-long-directory-name", 12)))));

        Assert.Equal(TestRendering.WIDTH, overflows.Width);
        Assert.True(overflows.Height > fits.Height);
    }

    private static StatusCard Status(string name, string detail) =>
        new()
        {
            Title = "状态",
            Time  = DateTimeOffset.UnixEpoch,
            Items =
            [
                new StatusItem
                {
                    Level  = CardLevel.Normal,
                    Name   = name,
                    Value  = "启用",
                    Detail = detail
                }
            ]
        };

    [Fact]
    public void RenderInfo_ItemsRowWrapsAcrossLines()
    {
        using SKBitmap few = Decode(TestRendering.Images.RenderInfo(InfoWithItems(2)));
        using SKBitmap many = Decode(TestRendering.Images.RenderInfo(InfoWithItems(40)));

        Assert.Equal(TestRendering.WIDTH, many.Width);
        Assert.True(many.Height > few.Height);
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

    [Fact]
    public void RenderDocument_HighlightsDarkEndfieldCodeBlockWithMultipleColors()
    {
        IReadOnlyList<IReadOnlyList<CodeToken>> tokens = CodeHighlighter.Highlight(
            "public sealed class Demo\n{\n    // 注释\n    public string Value = \"hello\";\n}\n",
            "csharp");

        HashSet<SKColor> distinctColors = [.. tokens.SelectMany(line => line).Select(token => token.Color)];
        Assert.True(distinctColors.Count >= 3);

        RenderedDocument rendered = TestRendering.Images.RenderDocument(new DocumentCard
        {
            Markdown = """
                ```csharp
                public sealed class Demo
                {
                    // 注释
                    public int Count => 42;
                }
                ```
                """,
            Note        = "gemini-4-hydrogen · medium",
            FooterRight = "[1/2]"
        });

        using SKBitmap bitmap = Decode(rendered.Image);
        Assert.Equal(TestRendering.WIDTH, bitmap.Width);
        Assert.True(HasPixelCloseTo(bitmap, CodeHighlighter.Background));
    }

    [Fact]
    public void CodeHighlighter_PrefersMoreSpecificScopePrefix()
    {
        SKColor keywordColor = CodeHighlighter.MapColor(["keyword.control.cs"]);
        SKColor operatorColor = CodeHighlighter.MapColor(["keyword.operator.assignment.cs"]);
        SKColor newOperatorColor = CodeHighlighter.MapColor(["keyword.operator.new.cs"]);
        Assert.Equal(keywordColor, newOperatorColor);
        Assert.NotEqual(operatorColor, newOperatorColor);

        SKColor typeColor = CodeHighlighter.MapColor(["support.type.cs"]);
        SKColor propertyColor = CodeHighlighter.MapColor(["variable.other.property.cs"]);
        SKColor jsonKeyColor = CodeHighlighter.MapColor(["support.type.property-name.json"]);
        Assert.Equal(propertyColor, jsonKeyColor);
        Assert.NotEqual(typeColor, jsonKeyColor);
    }

    [Fact]
    public void RenderDocument_RendersLatexFormulasAndFallsBackOnInvalidLatex()
    {
        string normalized = LatexMathRenderer.NormalizeDelimiters(
            "行内 \\(a^2 + b^2 = c^2\\) 与代码 `\\(skip\\)`\n\n```\n\\[skip\\]\n```\n\n\\[\n\\frac{1}{2}\n\\]");
        Assert.Contains("$a^2 + b^2 = c^2$", normalized);
        Assert.Contains("`\\(skip\\)`", normalized);
        Assert.Contains("\\[skip\\]", normalized);
        Assert.Contains("$$\n\\frac{1}{2}\n$$", normalized);

        using RenderedMathFormula? valid = LatexMathRenderer.TryRender(@"\frac{-b \pm \sqrt{b^2 - 4ac}}{2a}", 18, CardPalette.Ink, displayStyle: true);
        Assert.NotNull(valid);
        Assert.True(valid.Width > 20 && valid.Height > 15);

        using RenderedMathFormula? invalid = LatexMathRenderer.TryRender(@"\notarealmacro{\frac{1}", 18, CardPalette.Ink, displayStyle: false);
        Assert.Null(invalid);

        RenderedDocument rendered = TestRendering.Images.RenderDocument(new DocumentCard
        {
            Markdown = HostCommands.ExtractMarkdownSample("渲染测试")
        });
        using SKBitmap bitmap = Decode(rendered.Image);
        Assert.Equal(TestRendering.WIDTH, bitmap.Width);
        Assert.True(bitmap.Height > 900);
    }

    [Fact]
    public void HostCommands_ExtractMarkdownSampleSupportsDefaultAndCustomBody()
    {
        string defaultSample = HostCommands.ExtractMarkdownSample("渲染测试");
        Assert.Contains("# Markdown 渲染测试", defaultSample);
        Assert.Equal(defaultSample, HostCommands.ExtractMarkdownSample("md测试   "));

        string custom = HostCommands.ExtractMarkdownSample("md测试\n# 自定义标题\n$x+1$");
        Assert.Equal("# 自定义标题\n$x+1$", custom);
    }

    private static bool HasPixelCloseTo(SKBitmap bitmap, SKColor expected)
    {
        for (int y = 0; y < bitmap.Height; y += 4)
        {
            for (int x = 0; x < bitmap.Width; x += 4)
            {
                SKColor pixel = bitmap.GetPixel(x, y);
                if (Math.Abs(pixel.Red - expected.Red) <= 2
                    && Math.Abs(pixel.Green - expected.Green) <= 2
                    && Math.Abs(pixel.Blue - expected.Blue) <= 2)
                    return true;
            }
        }

        return false;
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

    private static InfoCard InfoWithItems(int count) =>
        new()
        {
            Label = "HSO",
            Title = "标签",
            Rows =
            [
                new CardRow
                {
                    Label      = "TAG",
                    Style      = CardRowStyle.Items,
                    Items      = [.. Enumerable.Range(1, count).Select(index => $"标签{index}")],
                    ItemBorder = true
                }
            ]
        };
}
