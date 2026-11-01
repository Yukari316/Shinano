using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Framework.Rendering.Models;
using SkiaSharp;

namespace Shinano.Host.Rendering;

/// <summary>
/// 宿主统一风格的文本长图：整张图即内容，不留外边距、不套卡片；标题区底色略深，有等级时左侧画强调色竖条；
/// 内容逐行排列，行间以淡分隔线隔开，标签为小号胶囊；底部灰色页脚。宽 720，按 2 倍像素绘制，长度随内容增加。
/// 配色取自面板暗色主题 daisyUI <c>dim</c>（5.7.50，由 oklch 换算为 sRGB）。线程安全：每次绘制使用独立的画布。
/// </summary>
public sealed class TextImageRenderer : IPluginImages
{
    private const float SCALE          = 2f;
    private const float WIDTH          = 720;
    private const float PADDING_X      = 32;
    private const float HEADER_TOP     = 28;
    private const float HEADER_BOTTOM  = 22;
    private const float SUBTITLE_GAP   = 6;
    private const float ACCENT_WIDTH   = 6;
    private const float TITLE_SIZE     = 28;
    private const float SUBTITLE_SIZE  = 15;
    private const float TEXT_SIZE      = 18;
    private const float LABEL_SIZE     = 13;
    private const float INDEX_SIZE     = 15;
    private const float TAG_SIZE       = 12;
    private const float FOOTER_SIZE    = 13;
    private const float TITLE_LEADING  = 1.3f;
    private const float LINE_SPACING   = 1.5f;
    private const float FIRST_BASELINE = 1.1f;
    private const float ROW_PADDING    = 14;
    private const float LABEL_GAP      = 2;
    private const float INDEX_GAP      = 16;
    private const float TAG_PADDING    = 8;
    private const float TAG_HEIGHT     = 22;
    private const float TAG_GAP        = 10;
    private const float TAG_RISE       = 0.82f;
    private const float TAG_BASELINE   = 0.36f;
    private const float FOOTER_PADDING = 16;
    private const int   MAX_TEXT_LINES = 12;
    private const byte  MUTED_ALPHA    = 150;
    private const byte  LABEL_ALPHA    = 140;
    private const byte  INDEX_ALPHA    = 110;
    private const byte  FOOTER_ALPHA   = 120;
    private const byte  LINE_ALPHA     = 22;
    private const byte  TAG_ALPHA      = 34;

    private static readonly SKColor Base100     = SKColor.Parse("#2A303C");
    private static readonly SKColor Base200     = SKColor.Parse("#242933");
    private static readonly SKColor BaseContent = SKColor.Parse("#B2CCD6");
    private static readonly SKColor Success     = SKColor.Parse("#62EFBD");
    private static readonly SKColor Warning     = SKColor.Parse("#EFD057");
    private static readonly SKColor Error       = SKColor.Parse("#FFAE9B");

    private readonly IPluginFonts _fonts;

    /// <summary>创建绘制器。</summary>
    public TextImageRenderer(IPluginFonts fonts)
    {
        _fonts = fonts;
    }

    /// <inheritdoc />
    public byte[] RenderTextImage(TextImageDocument document)
    {
        TextPainter painter = _fonts.Painter;
        SKTypeface regular = _fonts.Regular;
        SKTypeface bold = _fonts.Bold;

        // 序号列按最宽的序号对齐。
        float indexWidth = document.Rows
                                   .Select(row => row.Index is { } index ? painter.Measure(index.ToString(), regular, INDEX_SIZE) + INDEX_GAP : 0)
                                   .DefaultIfEmpty(0)
                                   .Max();
        List<LaidOutRow> rows = [.. document.Rows.Select(row => Layout(row, painter, regular, bold, indexWidth))];

        float headerHeight = HEADER_TOP + TITLE_SIZE * TITLE_LEADING
                             + (document.Subtitle is null ? 0 : SUBTITLE_GAP + SUBTITLE_SIZE * LINE_SPACING)
                             + HEADER_BOTTOM;
        float footerHeight = document.Footer is null ? 0 : FOOTER_PADDING * 2 + FOOTER_SIZE * LINE_SPACING;
        float height = headerHeight + rows.Sum(row => row.Height) + footerHeight;

        SKImageInfo info = new((int)Math.Ceiling(WIDTH * SCALE), (int)Math.Ceiling(height * SCALE), SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Scale(SCALE);
        canvas.Clear(Base100);

        DrawHeader(canvas, document, painter, regular, bold, headerHeight);
        float y = headerHeight;
        for (int index = 0; index < rows.Count; index++)
        {
            if (index > 0) DrawRule(canvas, PADDING_X, WIDTH - PADDING_X, y);
            DrawRow(canvas, rows[index], painter, regular, bold, y);
            y += rows[index].Height;
        }

        if (document.Footer is { } footer)
        {
            DrawRule(canvas, 0, WIDTH, y);
            using SKPaint paint = TextPaint(BaseContent.WithAlpha(FOOTER_ALPHA));
            painter.Draw(canvas, footer, regular, FOOTER_SIZE, PADDING_X, y + FOOTER_PADDING + FOOTER_SIZE, paint);
        }

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static LaidOutRow Layout(TextImageRow row, TextPainter painter, SKTypeface regular, SKTypeface bold, float indexWidth)
    {
        float tagWidth = row.Tag is { } tag ? painter.Measure(tag, bold, TAG_SIZE) + TAG_PADDING * 2 + TAG_GAP : 0;
        float textLeft = PADDING_X + (row.Index is null ? 0 : indexWidth) + tagWidth;
        IReadOnlyList<string> lines = painter.Wrap(row.Text, regular, TEXT_SIZE, WIDTH - PADDING_X - textLeft, MAX_TEXT_LINES);
        float labelHeight = row.Label is null ? 0 : LABEL_SIZE * LINE_SPACING + LABEL_GAP;
        return new LaidOutRow
        {
            Row         = row,
            Lines       = lines,
            IndexWidth  = row.Index is null ? 0 : indexWidth,
            TagWidth    = tagWidth,
            LabelHeight = labelHeight,
            Height      = ROW_PADDING * 2 + labelHeight + lines.Count * TEXT_SIZE * LINE_SPACING
        };
    }

    private static void DrawHeader(SKCanvas canvas, TextImageDocument document, TextPainter painter, SKTypeface regular, SKTypeface bold, float height)
    {
        using (SKPaint fill = new() { Color = Base200 }) canvas.DrawRect(0, 0, WIDTH, height, fill);
        if (Accent(document.Level) is { } accent)
            using (SKPaint fill = new() { Color = accent })
                canvas.DrawRect(0, 0, ACCENT_WIDTH, height, fill);

        float baseline = HEADER_TOP + TITLE_SIZE;
        using (SKPaint paint = TextPaint(BaseContent)) painter.Draw(canvas, document.Title, bold, TITLE_SIZE, PADDING_X, baseline, paint);
        if (document.Subtitle is not { } subtitle) return;

        baseline += SUBTITLE_GAP + SUBTITLE_SIZE * LINE_SPACING;
        using SKPaint muted = TextPaint(BaseContent.WithAlpha(MUTED_ALPHA));
        painter.Draw(canvas, subtitle, regular, SUBTITLE_SIZE, PADDING_X, baseline, muted);
    }

    private static void DrawRow(SKCanvas canvas, LaidOutRow laid, TextPainter painter, SKTypeface regular, SKTypeface bold, float top)
    {
        TextImageRow row = laid.Row;
        float y = top + ROW_PADDING;
        if (row.Label is { } label)
        {
            using SKPaint paint = TextPaint(BaseContent.WithAlpha(LABEL_ALPHA));
            painter.Draw(canvas, label, regular, LABEL_SIZE, PADDING_X, y + LABEL_SIZE, paint);
            y += laid.LabelHeight;
        }

        float baseline = y + TEXT_SIZE * FIRST_BASELINE;
        if (row.Index is { } index)
            using (SKPaint paint = TextPaint(BaseContent.WithAlpha(INDEX_ALPHA)))
                painter.Draw(canvas, index.ToString(), regular, INDEX_SIZE, PADDING_X, baseline, paint);

        float left = PADDING_X + laid.IndexWidth;
        if (row.Tag is { } tag)
        {
            float pillTop = baseline - TEXT_SIZE * TAG_RISE;
            SKRect pill = new(left, pillTop, left + laid.TagWidth - TAG_GAP, pillTop + TAG_HEIGHT);
            using (SKPaint fill = new() { Color = BaseContent.WithAlpha(TAG_ALPHA), IsAntialias = true })
                canvas.DrawRoundRect(pill, TAG_HEIGHT / 2, TAG_HEIGHT / 2, fill);
            using (SKPaint paint = TextPaint(BaseContent))
                painter.Draw(canvas, tag, bold, TAG_SIZE, left + TAG_PADDING, pillTop + TAG_HEIGHT / 2 + TAG_SIZE * TAG_BASELINE, paint);
            left += laid.TagWidth;
        }

        using SKPaint text = TextPaint(BaseContent);
        foreach (string line in laid.Lines)
        {
            painter.Draw(canvas, line, regular, TEXT_SIZE, left, baseline, text);
            baseline += TEXT_SIZE * LINE_SPACING;
        }
    }

    private static void DrawRule(SKCanvas canvas, float from, float to, float y)
    {
        using SKPaint line = new() { Color = BaseContent.WithAlpha(LINE_ALPHA), StrokeWidth = 1 };
        canvas.DrawLine(from, y, to, y, line);
    }

    private static SKPaint TextPaint(SKColor color) =>
        new()
        {
            Color       = color,
            IsAntialias = true
        };

    private static SKColor? Accent(TextImageLevel level) =>
        level switch
        {
            TextImageLevel.Normal  => Success,
            TextImageLevel.Warning => Warning,
            TextImageLevel.Error   => Error,
            _                      => null
        };

    private sealed class LaidOutRow
    {
        public required TextImageRow Row { get; init; }

        public required IReadOnlyList<string> Lines { get; init; }

        public required float IndexWidth { get; init; }

        public required float TagWidth { get; init; }

        public required float LabelHeight { get; init; }

        public required float Height { get; init; }
    }
}
