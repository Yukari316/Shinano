using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using SkiaSharp;

namespace Shinano.Host.Rendering;

/// <summary>
/// 消息图片的绘制上下文与通用版式（方案 C「反转终端」）：墨色反转标题块与黄色斜楔、带取景角的白色面板、左右两段的页脚与键值行。
/// 坐标为 720 宽的逻辑像素，输出时由 <see cref="CardRenderer" /> 按配置宽度整体缩放。每次绘制新建实例，不跨线程共享。
/// </summary>
internal sealed class CardCanvas
{
    /// <summary>逻辑版面宽度。</summary>
    public const float WIDTH = 720;

    /// <summary>面板距左右边缘的距离。</summary>
    public const float PANEL_MARGIN = 20;

    /// <summary>面板内边距（水平）。</summary>
    public const float PANEL_PADDING = 22;

    /// <summary>面板内容的左边界。</summary>
    public const float CONTENT_LEFT = PANEL_MARGIN + PANEL_PADDING;

    /// <summary>面板内容的右边界。</summary>
    public const float CONTENT_RIGHT = WIDTH - PANEL_MARGIN - PANEL_PADDING;

    /// <summary>录制时的高度上限（逻辑像素），远大于任何实际卡片。</summary>
    public const float MAX_HEIGHT = 100_000;

    private const float HEADER_PADDING_X  = 32;
    private const float HEADER_PADDING_Y  = 22;
    private const float META_SIZE         = 13;
    private const float TITLE_SIZE        = 32;
    private const float TITLE_LEADING     = 1.15f;
    private const float TITLE_GAP         = 12;
    private const float SUBTITLE_SIZE     = 15;
    private const float SUBTITLE_GAP      = 8;
    private const int   TITLE_MAX_LINES   = 3;
    private const float DIAMOND_SIZE      = 9;
    private const float DIAMOND_GAP       = 9;
    private const float EDGE_HEIGHT       = 6;
    private const float EDGE_TOP_RATIO    = 0.46f;
    private const float EDGE_BOTTOM_RATIO = 0.44f;
    private const float PANEL_GAP         = 26;
    private const float PANEL_PADDING_Y   = 8;
    private const float BRACKET_SIZE      = 14;
    private const float BRACKET_STROKE    = 2;
    private const float BRACKET_OFFSET    = 6;
    private const float FOOTER_GAP        = 14;
    private const float FOOTER_SIZE       = 12;
    private const float HINT_SIZE         = 14;
    private const float BOTTOM_PADDING    = 20;
    private const float ROW_PADDING       = 14;
    private const float LABEL_WIDTH       = 64;
    private const float LABEL_GAP         = 16;
    private const float LABEL_SIZE        = 14;
    private const float BODY_SIZE         = 18;
    private const float BODY_LEADING      = 1.5f;
    private const float RULE_WIDTH        = 1;
    private const float ITEM_SIZE         = 15;
    private const float ITEM_BOX_HEIGHT   = 26;
    private const float ITEM_PADDING_X    = 8;
    private const float ITEM_STROKE       = 1.5f;
    private const float ITEM_BOX_GAP      = 8;
    private const float ITEM_TEXT_GAP     = 16;
    private const float ITEM_LINE_GAP     = 6;

    private CardCanvas(SKCanvas canvas, IPluginFonts fonts)
    {
        Canvas  = canvas;
        Fonts   = fonts;
        Painter = fonts.Painter;
    }

    /// <summary>底层画布。</summary>
    public SKCanvas Canvas { get; }

    /// <summary>公共字体。</summary>
    public IPluginFonts Fonts { get; }

    /// <summary>文字绘制辅助。</summary>
    public TextPainter Painter { get; }

    /// <summary>录制一段绘制，返回绘制指令与 <paramref name="draw" /> 报告的高度；坐标原点不变。</summary>
    public static (SKPicture Picture, float Height) Record(IPluginFonts fonts, Func<CardCanvas, float> draw)
    {
        using SKPictureRecorder recorder = new();
        SKCanvas canvas = recorder.BeginRecording(new SKRect(0, 0, WIDTH, MAX_HEIGHT));
        float height = draw(new CardCanvas(canvas, fonts));
        return (recorder.EndRecording(), height);
    }

    /// <summary>文字基线：在高 <c>size × leading</c> 的行框中垂直居中。</summary>
    public static float Baseline(float top, float size, float leading) => top + (size * leading - size) / 2 + size * 0.88f;

    /// <summary>在基线处绘制一行文字。</summary>
    public void Text(string text, SKTypeface typeface, float size, SKColor color, float x, float baseline)
    {
        using SKPaint paint = TextPaint(color);
        Painter.Draw(Canvas, text, typeface, size, x, baseline, paint);
    }

    /// <summary>右对齐绘制一行文字。</summary>
    public void TextRight(string text, SKTypeface typeface, float size, SKColor color, float right, float baseline) =>
        Text(text, typeface, size, color, right - Painter.Measure(text, typeface, size), baseline);

    /// <summary>按宽度换行后逐行绘制，返回所占高度。</summary>
    public float Paragraph(string text, SKTypeface typeface, float size, float leading, SKColor color, float left, float right, float top, int maxLines = int.MaxValue)
    {
        IReadOnlyList<string> lines = Painter.Wrap(text, typeface, size, right - left, maxLines);
        float lineHeight = size * leading;
        for (int index = 0; index < lines.Count; index++)
            Text(lines[index], typeface, size, color, left, Baseline(top + index * lineHeight, size, leading));
        return lines.Count * lineHeight;
    }

    /// <summary>填充矩形。</summary>
    public void Fill(SKRect rect, SKColor color)
    {
        using SKPaint paint = new()
        {
            Color       = color,
            IsAntialias = true
        };
        Canvas.DrawRect(rect, paint);
    }

    /// <summary>画水平线。</summary>
    public void Rule(float left, float right, float y, SKColor color, float width = RULE_WIDTH) =>
        Fill(new SKRect(left, y, right, y + width), color);

    /// <summary>画以 (<paramref name="cx" />, <paramref name="cy" />) 为中心、边长 <paramref name="size" /> 的菱形。</summary>
    public void Diamond(float cx, float cy, float size, SKColor color)
    {
        float half = size * MathF.Sqrt(2) / 2;
        Polygon(color, new SKPoint(cx, cy - half), new SKPoint(cx + half, cy), new SKPoint(cx, cy + half), new SKPoint(cx - half, cy));
    }

    /// <summary>填充多边形。</summary>
    public void Polygon(SKColor color, params SKPoint[] points)
    {
        using SKPathBuilder builder = new();
        builder.AddPoly(points);
        using SKPath path = builder.Detach();
        using SKPaint paint = new()
        {
            Color       = color,
            IsAntialias = true
        };
        Canvas.DrawPath(path, paint);
    }

    /// <summary>
    /// 墨色反转标题块：左上菱形与英文类型标签（按等级取色），右上说明，中文标题（最多三行）与可选副标题，下缘黄色斜楔。
    /// </summary>
    /// <returns>标题块底边（不含斜楔）。</returns>
    public float Header(string label, SKColor labelColor, string? meta, string title, string? subtitle)
    {
        float left = HEADER_PADDING_X;
        float right = WIDTH - HEADER_PADDING_X;
        IReadOnlyList<string> titleLines = Painter.Wrap(title, Fonts.Bold, TITLE_SIZE, right - left, TITLE_MAX_LINES);
        float metaHeight = META_SIZE * TITLE_LEADING;
        float titleHeight = titleLines.Count * TITLE_SIZE * TITLE_LEADING;
        float subtitleHeight = subtitle is null ? 0 : SUBTITLE_GAP + SUBTITLE_SIZE * BODY_LEADING;
        float bottom = HEADER_PADDING_Y + metaHeight + TITLE_GAP + titleHeight + subtitleHeight + HEADER_PADDING_Y;

        Fill(new SKRect(0, 0, WIDTH, bottom), CardPalette.Ink);
        float metaBaseline = Baseline(HEADER_PADDING_Y, META_SIZE, TITLE_LEADING);
        Diamond(left + DIAMOND_SIZE / 2, HEADER_PADDING_Y + metaHeight / 2, DIAMOND_SIZE, labelColor);
        Text(label, Fonts.Latin, META_SIZE, labelColor, left + DIAMOND_SIZE + DIAMOND_GAP, metaBaseline);
        if (meta is not null) TextRight(meta, Fonts.Latin, META_SIZE, CardPalette.LineStrong, right, metaBaseline);

        float y = HEADER_PADDING_Y + metaHeight + TITLE_GAP;
        foreach (string line in titleLines)
        {
            Text(line, Fonts.Bold, TITLE_SIZE, CardPalette.Paper, left, Baseline(y, TITLE_SIZE, TITLE_LEADING));
            y += TITLE_SIZE * TITLE_LEADING;
        }

        if (subtitle is not null)
            Text(subtitle, Fonts.Regular, SUBTITLE_SIZE, CardPalette.LineStrong, left, Baseline(y + SUBTITLE_GAP, SUBTITLE_SIZE, BODY_LEADING));

        Edge(bottom);
        return bottom;
    }

    /// <summary>贴在 <paramref name="top" /> 下方的信号黄斜楔。</summary>
    public void Edge(float top) =>
        Polygon(
            CardPalette.Signal,
            new SKPoint(0, top),
            new SKPoint(WIDTH * EDGE_TOP_RATIO, top),
            new SKPoint(WIDTH * EDGE_BOTTOM_RATIO, top + EDGE_HEIGHT),
            new SKPoint(0, top + EDGE_HEIGHT));

    /// <summary>
    /// 在 <paramref name="top" /> 之后留出间距，画一块带四角取景角的白色面板；<paramref name="content" /> 以面板内容的顶边为 y=0 绘制并返回高度。
    /// </summary>
    /// <returns>面板底边。</returns>
    public float Panel(float top, Func<CardCanvas, float> content, float paddingY = PANEL_PADDING_Y, float gap = PANEL_GAP)
    {
        (SKPicture picture, float height) = Record(Fonts, content);
        using (picture)
        {
            SKRect rect = new(PANEL_MARGIN, top + gap, WIDTH - PANEL_MARGIN, top + gap + paddingY * 2 + height);
            Fill(rect, CardPalette.Paper);
            Brackets(rect);
            Canvas.Save();
            Canvas.Translate(0, rect.Top + paddingY);
            Canvas.DrawPicture(picture);
            Canvas.Restore();
            return rect.Bottom;
        }
    }

    /// <summary>
    /// 页脚：左右两侧各一段可选文字，纯 ASCII 的（如英文代号）用英文小字，其余用正文字体；两侧都没有时只留出底边距。
    /// </summary>
    /// <returns>整张卡片的底边。</returns>
    public float Footer(float top, string? left, string? right)
    {
        float baseline = Baseline(top + FOOTER_GAP, HINT_SIZE, BODY_LEADING);
        if (!string.IsNullOrEmpty(left)) FooterText(left, baseline, alignRight: false);
        if (!string.IsNullOrEmpty(right)) FooterText(right, baseline, alignRight: true);
        return top + FOOTER_GAP + HINT_SIZE * BODY_LEADING + BOTTOM_PADDING;
    }

    private void FooterText(string text, float baseline, bool alignRight)
    {
        bool latin = text.All(char.IsAscii);
        SKTypeface typeface = latin ? Fonts.Latin : Fonts.Regular;
        float size = latin ? FOOTER_SIZE : HINT_SIZE;
        SKColor color = latin ? CardPalette.InkTertiary : CardPalette.InkSecondary;
        if (alignRight) TextRight(text, typeface, size, color, WIDTH - PANEL_MARGIN, baseline);
        else Text(text, typeface, size, color, PANEL_MARGIN, baseline);
    }

    /// <summary>键值行：左侧标签、右侧可换行的内容，下方分隔线（最后一行除外）；返回行底。</summary>
    public float KeyValue(float top, string label, string text, bool last)
    {
        float textLeft = CONTENT_LEFT + LABEL_WIDTH + LABEL_GAP;
        float y = top + ROW_PADDING;
        Text(label, Fonts.Regular, LABEL_SIZE, CardPalette.InkSecondary, CONTENT_LEFT, Baseline(y, BODY_SIZE, BODY_LEADING));
        float height = Paragraph(text, Fonts.Regular, BODY_SIZE, BODY_LEADING, CardPalette.Ink, textLeft, CONTENT_RIGHT, y);
        float bottom = y + height + ROW_PADDING;
        if (!last) Rule(CONTENT_LEFT, CONTENT_RIGHT, bottom - RULE_WIDTH, CardPalette.Line);
        return bottom;
    }

    /// <summary>
    /// 元素行：左侧标签，右侧元素从左到右排列、放不下时换到下一行，<paramref name="bordered" /> 时每个元素画墨色直角方框；
    /// 宽于一行的元素截断并以省略号结尾。下方分隔线（最后一行除外）；返回行底。
    /// </summary>
    public float ItemsRow(float top, string label, IReadOnlyList<string> items, bool bordered, bool last)
    {
        float left = CONTENT_LEFT + LABEL_WIDTH + LABEL_GAP;
        float padding = bordered ? ITEM_PADDING_X : 0;
        float gap = bordered ? ITEM_BOX_GAP : ITEM_TEXT_GAP;
        float lineHeight = BODY_SIZE * BODY_LEADING;
        float y = top + ROW_PADDING;
        Text(label, Fonts.Regular, LABEL_SIZE, CardPalette.InkSecondary, CONTENT_LEFT, Baseline(y, BODY_SIZE, BODY_LEADING));

        using SKPaint stroke = new()
        {
            Color       = CardPalette.Ink,
            IsStroke    = true,
            StrokeWidth = ITEM_STROKE,
            IsAntialias = true
        };
        float x = left;
        float lineTop = y;
        foreach (string item in items)
        {
            string text = Painter.Wrap(item, Fonts.Regular, ITEM_SIZE, CONTENT_RIGHT - left - padding * 2, 1) is [{ } first, ..] ? first : string.Empty;
            float width = Painter.Measure(text, Fonts.Regular, ITEM_SIZE) + padding * 2;
            if (x > left && x + width > CONTENT_RIGHT)
            {
                x = left;
                lineTop += lineHeight + ITEM_LINE_GAP;
            }

            float boxTop = lineTop + (lineHeight - ITEM_BOX_HEIGHT) / 2;
            if (bordered) Canvas.DrawRect(new SKRect(x, boxTop, x + width, boxTop + ITEM_BOX_HEIGHT), stroke);
            Text(text, Fonts.Regular, ITEM_SIZE, CardPalette.Ink, x + padding, Baseline(boxTop, ITEM_SIZE, ITEM_BOX_HEIGHT / ITEM_SIZE));
            x += width + gap;
        }

        float bottom = lineTop + lineHeight + ROW_PADDING;
        if (!last) Rule(CONTENT_LEFT, CONTENT_RIGHT, bottom - RULE_WIDTH, CardPalette.Line);
        return bottom;
    }

    private void Brackets(SKRect rect)
    {
        float left = rect.Left - BRACKET_OFFSET;
        float right = rect.Right + BRACKET_OFFSET;
        float top = rect.Top - BRACKET_OFFSET;
        float bottom = rect.Bottom + BRACKET_OFFSET;
        SKColor ink = CardPalette.Ink;
        Fill(new SKRect(left, top, left + BRACKET_SIZE, top + BRACKET_STROKE), ink);
        Fill(new SKRect(left, top, left + BRACKET_STROKE, top + BRACKET_SIZE), ink);
        Fill(new SKRect(right - BRACKET_SIZE, top, right, top + BRACKET_STROKE), ink);
        Fill(new SKRect(right - BRACKET_STROKE, top, right, top + BRACKET_SIZE), ink);
        Fill(new SKRect(left, bottom - BRACKET_STROKE, left + BRACKET_SIZE, bottom), ink);
        Fill(new SKRect(left, bottom - BRACKET_SIZE, left + BRACKET_STROKE, bottom), ink);
        Fill(new SKRect(right - BRACKET_SIZE, bottom - BRACKET_STROKE, right, bottom), ink);
        Fill(new SKRect(right - BRACKET_STROKE, bottom - BRACKET_SIZE, right, bottom), ink);
    }

    private static SKPaint TextPaint(SKColor color) =>
        new()
        {
            Color       = color,
            IsAntialias = true
        };
}
