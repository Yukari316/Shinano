using SkiaSharp;

namespace Shinano.Host.Notifications;

/// <summary>
/// 把通知卡片绘制为 PNG。配色取自面板暗色主题 daisyUI <c>dim</c>（5.7.50，由 oklch 换算为 sRGB），
/// 字体为随宿主交付的 Noto Sans SC。按 2 倍像素绘制，在手机上也清晰。线程安全：每次绘制使用独立的画布。
/// </summary>
public sealed class NotificationCardRenderer : IDisposable
{
    private const float SCALE          = 2f;
    private const float WIDTH          = 560;
    private const float OUTER_PADDING  = 16;
    private const float CARD_PADDING   = 24;
    private const float CARD_RADIUS    = 16;
    private const float ACCENT_HEIGHT  = 4;
    private const float ICON_SIZE      = 44;
    private const float ICON_RADIUS    = 12;
    private const float HEADER_GAP     = 14;
    private const float TITLE_SIZE     = 21;
    private const float CAPTION_SIZE   = 13;
    private const float LABEL_SIZE     = 13;
    private const float VALUE_SIZE     = 15;
    private const float BADGE_SIZE     = 13;
    private const float BADGE_PADDING  = 10;
    private const float BADGE_HEIGHT   = 24;
    private const float LINE_SPACING   = 1.45f;
    private const float ITEM_GAP       = 12;
    private const float SECTION_GAP    = 18;
    private const int   MAX_VALUE_LINES = 8;
    private const byte  MUTED_ALPHA    = 150;
    private const byte  LINE_ALPHA     = 36;
    private const string ELLIPSIS      = "…";

    private static readonly SKColor Base100     = SKColor.Parse("#2A303C");
    private static readonly SKColor Base300     = SKColor.Parse("#20252E");
    private static readonly SKColor BaseContent = SKColor.Parse("#B2CCD6");
    private static readonly SKColor Error       = SKColor.Parse("#FFAE9B");
    private static readonly SKColor ErrorText   = SKColor.Parse("#160B09");
    private static readonly SKColor Warning     = SKColor.Parse("#EFD057");
    private static readonly SKColor WarningText = SKColor.Parse("#141003");
    private static readonly SKColor Success     = SKColor.Parse("#62EFBD");
    private static readonly SKColor SuccessText = SKColor.Parse("#03140D");

    private readonly SKTypeface _regular;
    private readonly SKTypeface _bold;
    private readonly SKImage?   _icon;

    /// <summary>从 <paramref name="resourceDirectory" /> 读取字体与图标。</summary>
    /// <exception cref="FileNotFoundException">字体文件缺失。</exception>
    public NotificationCardRenderer(string resourceDirectory)
    {
        _regular = LoadTypeface(Path.Combine(resourceDirectory, "Fonts", "NotoSansSC-Regular.otf"));
        _bold    = LoadTypeface(Path.Combine(resourceDirectory, "Fonts", "NotoSansSC-Bold.otf"));
        string iconPath = Path.Combine(resourceDirectory, "icon.png");
        _icon = File.Exists(iconPath) ? SKImage.FromEncodedData(iconPath) : null;
    }

    /// <summary>绘制卡片。</summary>
    /// <param name="card">卡片内容。</param>
    /// <param name="time">显示的时间，按部署时区换算后的本地时间。</param>
    /// <returns>PNG 数据。</returns>
    public byte[] Render(NotificationCard card, DateTimeOffset time)
    {
        using SKFont title   = new(_bold, TITLE_SIZE, 1, 0);
        using SKFont caption = new(_regular, CAPTION_SIZE, 1, 0);
        using SKFont label   = new(_regular, LABEL_SIZE, 1, 0);
        using SKFont value   = new(_regular, VALUE_SIZE, 1, 0);
        using SKFont badge   = new(_bold, BADGE_SIZE, 1, 0);

        float cardWidth    = WIDTH - OUTER_PADDING * 2;
        float contentWidth = cardWidth - CARD_PADDING * 2;
        (SKColor accent, SKColor accentText, string badgeText) = Level(card.Level);
        float badgeWidth   = badge.MeasureText(badgeText, null) + BADGE_PADDING * 2;
        float titleWidth   = contentWidth - ICON_SIZE - HEADER_GAP - badgeWidth - HEADER_GAP;

        List<string> titleLines = Wrap(card.Title, title, titleWidth, maxLines: 2);
        float titleLineHeight   = TITLE_SIZE * LINE_SPACING;
        float headerHeight      = Math.Max(ICON_SIZE, titleLines.Count * titleLineHeight + CAPTION_SIZE * LINE_SPACING);
        List<(string Label, List<string> Lines)> items =
        [
            .. card.Items.Select(item => (item.Label, Wrap(item.Value, value, contentWidth, MAX_VALUE_LINES)))
        ];
        float itemsHeight = items.Sum(item => LABEL_SIZE * LINE_SPACING + item.Lines.Count * VALUE_SIZE * LINE_SPACING)
                            + Math.Max(0, items.Count - 1) * ITEM_GAP;
        float cardHeight  = ACCENT_HEIGHT + CARD_PADDING + headerHeight
                            + (items.Count > 0 ? SECTION_GAP * 2 + itemsHeight : 0) + CARD_PADDING;
        float height      = cardHeight + OUTER_PADDING * 2;

        SKImageInfo info = new((int)Math.Ceiling(WIDTH * SCALE), (int)Math.Ceiling(height * SCALE), SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Scale(SCALE);
        canvas.Clear(Base300);
        using SKPaint paint = new() { IsAntialias = true };

        SKRect cardRect = SKRect.Create(OUTER_PADDING, OUTER_PADDING, cardWidth, cardHeight);
        using SKRoundRect cardShape = new(cardRect, CARD_RADIUS);
        canvas.Save();
        canvas.ClipRoundRect(cardShape, antialias: true);
        paint.Color = Base100;
        canvas.DrawRect(cardRect, paint);
        paint.Color = accent;
        canvas.DrawRect(SKRect.Create(cardRect.Left, cardRect.Top, cardWidth, ACCENT_HEIGHT), paint);
        canvas.Restore();

        float left = cardRect.Left + CARD_PADDING;
        float top  = cardRect.Top + ACCENT_HEIGHT + CARD_PADDING;
        DrawIcon(canvas, paint, left, top);

        float textLeft = left + ICON_SIZE + HEADER_GAP;
        paint.Color = BaseContent;
        float baseline = top;
        foreach (string line in titleLines)
        {
            baseline += titleLineHeight;
            canvas.DrawText(line, textLeft, baseline - (titleLineHeight - TITLE_SIZE) / 2, SKTextAlign.Left, title, paint);
        }

        paint.Color = BaseContent.WithAlpha(MUTED_ALPHA);
        canvas.DrawText($"Shinano · {time:yyyy-MM-dd HH:mm:ss}", textLeft, baseline + CAPTION_SIZE * LINE_SPACING - 3, SKTextAlign.Left, caption, paint);

        float badgeLeft = cardRect.Right - CARD_PADDING - badgeWidth;
        paint.Color = accent;
        canvas.DrawRoundRect(SKRect.Create(badgeLeft, top + 2, badgeWidth, BADGE_HEIGHT), BADGE_HEIGHT / 2, BADGE_HEIGHT / 2, paint);
        paint.Color = accentText;
        canvas.DrawText(badgeText, badgeLeft + BADGE_PADDING, top + 2 + BADGE_HEIGHT / 2 + BADGE_SIZE * 0.36f, SKTextAlign.Left, badge, paint);

        if (items.Count == 0) return Encode(surface);

        float y = top + headerHeight + SECTION_GAP;
        paint.Color = BaseContent.WithAlpha(LINE_ALPHA);
        canvas.DrawRect(SKRect.Create(left, y, contentWidth, 1), paint);
        y += SECTION_GAP;
        foreach ((string itemLabel, List<string> lines) in items)
        {
            y += LABEL_SIZE * LINE_SPACING;
            paint.Color = BaseContent.WithAlpha(MUTED_ALPHA);
            canvas.DrawText(itemLabel, left, y - 4, SKTextAlign.Left, label, paint);
            paint.Color = BaseContent;
            foreach (string line in lines)
            {
                y += VALUE_SIZE * LINE_SPACING;
                canvas.DrawText(line, left, y - 5, SKTextAlign.Left, value, paint);
            }

            y += ITEM_GAP;
        }

        return Encode(surface);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _regular.Dispose();
        _bold.Dispose();
        _icon?.Dispose();
    }

    private void DrawIcon(SKCanvas canvas, SKPaint paint, float left, float top)
    {
        SKRect rect = SKRect.Create(left, top, ICON_SIZE, ICON_SIZE);
        using SKRoundRect shape = new(rect, ICON_RADIUS);
        canvas.Save();
        canvas.ClipRoundRect(shape, antialias: true);
        if (_icon is not null)
        {
            canvas.DrawImage(_icon, rect, new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        }
        else
        {
            paint.Color = Base300;
            canvas.DrawRect(rect, paint);
        }

        canvas.Restore();
    }

    private static (SKColor Accent, SKColor Text, string Badge) Level(NotificationLevel level) =>
        level switch
        {
            NotificationLevel.Error   => (Error, ErrorText, "错误"),
            NotificationLevel.Warning => (Warning, WarningText, "警告"),
            _                         => (Success, SuccessText, "正常")
        };

    // 按宽度逐字折行，英文优先在空格处断开；超过行数时末行以省略号结尾。
    private static List<string> Wrap(string text, SKFont font, float width, int maxLines)
    {
        List<string> lines = [];
        foreach (string paragraph in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string rest = paragraph;
            do
            {
                int count = Math.Max(1, font.BreakText(rest, width, null));
                if (count < rest.Length)
                {
                    if (char.IsHighSurrogate(rest[count - 1])) count--;
                    int space = rest.LastIndexOf(' ', count - 1, count);
                    if (space > count / 2 && !char.IsWhiteSpace(rest[count])) count = space + 1;
                }

                lines.Add(rest[..count].TrimEnd());
                rest = rest[count..].TrimStart();
            } while (rest.Length > 0);
        }

        if (lines.Count <= maxLines) return lines;

        lines = lines[..maxLines];
        string last = lines[^1];
        while (last.Length > 0 && font.MeasureText(last + ELLIPSIS, null) > width) last = last[..^1];
        lines[^1] = last + ELLIPSIS;
        return lines;
    }

    private static byte[] Encode(SKSurface surface)
    {
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKTypeface LoadTypeface(string path) =>
        File.Exists(path)
            ? SKTypeface.FromFile(path, 0)
            : throw new FileNotFoundException("通知卡片字体缺失", path);
}
