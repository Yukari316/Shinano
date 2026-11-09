using System.Globalization;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Framework.Rendering.Models;
using Shinano.Plugin.Bilibili.Models;
using SkiaSharp;

namespace Shinano.Plugin.Bilibili;

/// <summary>
/// 把动态绘制为宿主风格的框架卡片：标题块为头像、UP 名与动作，内容区为标题、富文本正文、缩略图网格、视频封面与 BV 号、
/// 转发的原动态与附加卡片。不画链接。图片先下载再绘制，单张失败时画占位块。线程安全。
/// </summary>
public sealed class BiliCardRenderer
{
    private const string LABEL_PREFIX    = "BILIBILI · ";
    private const string FOOTER_LEFT     = "BILIBILI";
    private const string TIME_FORMAT     = "yyyy-MM-dd HH:mm";
    private const string CHINA_TIME_ZONE = "Asia/Shanghai";
    private const string IMAGE_FAILED    = "图片加载失败";
    private const string NEWLINE         = "\n";
    private const string CRLF            = "\r\n";
    private const float  PAD_Y           = 18;
    private const float  GAP             = 14;
    private const float  TITLE_EXTRA     = 4;
    private const float  BODY_SIZE       = 18;
    private const float  ORIGIN_SIZE     = 16;
    private const float  LEADING         = 1.5f;
    private const float  GRID_GAP        = 6;
    private const int    GRID_MAX        = 9;
    private const float  SINGLE_MAX      = 2;
    private const float  COVER_RATIO     = 9f / 16;
    private const float  BADGE_SIZE      = 14;
    private const float  BADGE_PAD       = 8;
    private const float  BADGE_HEIGHT    = 24;
    private const float  BADGE_MARGIN    = 10;
    private const float  VIDEO_TITLE     = 20;
    private const float  VIDEO_DESC      = 15;
    private const int    DESC_LINES      = 3;
    private const float  LABEL_SIZE      = 14;
    private const float  LABEL_WIDTH     = 64;
    private const float  ORIGIN_BAR      = 4;
    private const float  ORIGIN_PAD      = 14;
    private const float  ORIGIN_NAME     = 16;
    private const float  ORIGIN_ACTION   = 13;
    private const float  ATTACH_SIZE     = 15;
    private const float  OVERLAY_SIZE    = 30;
    private const byte   OVERLAY_ALPHA   = 140;
    private const int    MAX_TEXT_LINES  = 40;
    private const int    THUMB_PIXELS    = 480;
    private const int    FULL_PIXELS     = 1080;
    private const int    AVATAR_PIXELS   = 120;

    private static readonly TimeZoneInfo ChinaTime =
        TimeZoneInfo.TryFindSystemTimeZoneById(CHINA_TIME_ZONE, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.CreateCustomTimeZone(CHINA_TIME_ZONE, TimeSpan.FromHours(8), CHINA_TIME_ZONE, CHINA_TIME_ZONE);

    private readonly IPluginInitializationContext _context;
    private readonly BiliHttp                     _http;
    private readonly ILogger<BiliCardRenderer>    _logger;

    /// <summary>创建绘制器。</summary>
    public BiliCardRenderer(IPluginInitializationContext context, BiliHttp http, ILogger<BiliCardRenderer> logger)
    {
        _context = context;
        _http    = http;
        _logger  = logger;
    }

    /// <summary>绘制一条动态的卡片。</summary>
    /// <returns>PNG 字节。</returns>
    public async Task<byte[]> RenderAsync(BiliDynamic dynamic, CancellationToken cancellationToken)
    {
        Dictionary<string, SKImage?> images = [];
        try
        {
            foreach (string url in ImageUrls(dynamic).Distinct())
                images[url] = await LoadAsync(url, cancellationToken);

            byte[]? avatar = dynamic.AuthorFace.Length > 0 && images.GetValueOrDefault(Sized(dynamic.AuthorFace, AVATAR_PIXELS, square: true)) is { } face
                ? face.Encode(SKEncodedImageFormat.Png, 100).ToArray()
                : null;
            return _context.Images.RenderFramed(
                new FramedCard
                {
                    Label       = LABEL_PREFIX + Label(dynamic.Kind),
                    Meta        = TimeZoneInfo.ConvertTime(dynamic.PublishedAt, ChinaTime).ToString(TIME_FORMAT, CultureInfo.InvariantCulture),
                    Title       = dynamic.AuthorName,
                    Subtitle    = dynamic.Action,
                    Avatar      = avatar,
                    DrawContent = (canvas, width) => new Layout(canvas, _context.Fonts, images).Card(dynamic, width),
                    FooterLeft  = FOOTER_LEFT,
                    FooterRight = $"UID {dynamic.AuthorId}"
                });
        }
        finally
        {
            foreach (SKImage? image in images.Values) image?.Dispose();
        }
    }

    private static string Label(BiliDynamicKind kind) =>
        kind switch
        {
            BiliDynamicKind.Opus    => "OPUS",
            BiliDynamicKind.Article => "ARTICLE",
            BiliDynamicKind.Video   => "VIDEO",
            BiliDynamicKind.Forward => "REPOST",
            _                       => "DYNAMIC"
        };

    // 卡片用到的图片都按显示尺寸向 B站图片服务请求缩小后的版本，原图只在推送时由 IM 端下载。
    private static IEnumerable<string> ImageUrls(BiliDynamic dynamic)
    {
        if (dynamic.AuthorFace.Length > 0) yield return Sized(dynamic.AuthorFace, AVATAR_PIXELS, square: true);
        foreach (string url in ContentUrls(dynamic)) yield return url;
        if (dynamic.Original is { } original)
            foreach (string url in ContentUrls(original))
                yield return url;
    }

    private static IEnumerable<string> ContentUrls(BiliDynamic dynamic)
    {
        if (dynamic.Video is { Cover.Length: > 0 } video) yield return Sized(video.Cover, FULL_PIXELS, square: false);
        int pixels = dynamic.Pictures.Count == 1 ? FULL_PIXELS : THUMB_PIXELS;
        foreach (BiliPicture picture in dynamic.Pictures.Take(GRID_MAX)) yield return Sized(picture.Url, pixels, square: false);
    }

    /// <summary>B站图片服务的缩放后缀：按宽度缩小（或裁成正方形）并转为 WebP；已带参数的地址不变。</summary>
    internal static string Sized(string url, int pixels, bool square) =>
        url.Contains('@', StringComparison.Ordinal) ? url : square ? $"{url}@{pixels}w_{pixels}h_1c.webp" : $"{url}@{pixels}w.webp";

    private async Task<SKImage?> LoadAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            SKImage? image = SKImage.FromEncodedData(await _http.DownloadAsync(url, cancellationToken));
            if (image is null) _logger.LogWarning("动态图片无法解码：{Url}", url);
            return image;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "下载动态图片失败：{Url}", url);
            return null;
        }
    }

    /// <summary>一次绘制的版面：坐标以内容区左上为原点，按 720 宽的逻辑版面。</summary>
    private sealed class Layout
    {
        private readonly SKCanvas                              _canvas;
        private readonly IPluginFonts                          _fonts;
        private readonly IReadOnlyDictionary<string, SKImage?> _images;

        public Layout(SKCanvas canvas, IPluginFonts fonts, IReadOnlyDictionary<string, SKImage?> images)
        {
            _canvas = canvas;
            _fonts  = fonts;
            _images = images;
        }

        public float Card(BiliDynamic dynamic, float width) => Body(dynamic, 0, width, PAD_Y, BODY_SIZE) + PAD_Y;

        // 依次绘制标题、正文、视频、图片、原动态与附加卡片，返回底边；各部分之间留固定间距。
        private float Body(BiliDynamic dynamic, float left, float width, float top, float bodySize)
        {
            float y = top;
            bool first = true;
            float Next() => first ? y : y + GAP;

            if (dynamic.Title is { } title)
            {
                y = Paragraph(title, _fonts.Bold, bodySize + TITLE_EXTRA, CardPalette.Ink, left, width, Next(), MAX_TEXT_LINES);
                first = false;
            }

            if (dynamic.Text.Count > 0)
            {
                y = Rich(dynamic.Text, left, width, Next(), bodySize);
                first = false;
            }

            if (dynamic.Video is { } video)
            {
                y = Video(video, left, width, Next());
                first = false;
            }

            if (dynamic.Pictures.Count > 0)
            {
                y = Grid(dynamic.Pictures, left, width, Next());
                first = false;
            }

            if (dynamic.Original is { } original)
            {
                y = Origin(original, left, width, Next());
                first = false;
            }
            else if (dynamic.Kind == BiliDynamicKind.Forward)
            {
                y = Paragraph("原动态已删除", _fonts.Regular, bodySize, CardPalette.InkTertiary, left, width, Next(), 1);
                first = false;
            }

            if (dynamic.Attachment is { } attachment)
            {
                float ruleTop = Next();
                Rule(left, left + width, ruleTop);
                y = Paragraph(attachment, _fonts.Regular, ATTACH_SIZE, CardPalette.InkSecondary, left, width, ruleTop + GAP / 2, 2);
            }

            return y;
        }

        private float Origin(BiliDynamic original, float left, float width, float top)
        {
            float innerLeft = left + ORIGIN_BAR + ORIGIN_PAD;
            float innerWidth = width - ORIGIN_BAR - ORIGIN_PAD * 2;
            using SKPictureRecorder recorder = new();
            Layout inner = new(recorder.BeginRecording(new SKRect(0, 0, width, 100_000)), _fonts, _images);
            float nameBaseline = Baseline(ORIGIN_PAD, ORIGIN_NAME);
            inner.Text(original.AuthorName, _fonts.Bold, ORIGIN_NAME, CardPalette.Ink, innerLeft, nameBaseline);
            float nameWidth = _fonts.Painter.Measure(original.AuthorName, _fonts.Bold, ORIGIN_NAME);
            inner.Text(original.Action, _fonts.Regular, ORIGIN_ACTION, CardPalette.InkTertiary, innerLeft + nameWidth + 8, nameBaseline);
            float bottom = inner.Body(original, innerLeft, innerWidth, ORIGIN_PAD + ORIGIN_NAME * LEADING + GAP / 2, ORIGIN_SIZE) + ORIGIN_PAD;
            using SKPicture picture = recorder.EndRecording();

            Fill(new SKRect(left, top, left + width, top + bottom), CardPalette.Sunken);
            Fill(new SKRect(left, top, left + ORIGIN_BAR, top + bottom), CardPalette.Ink);
            _canvas.Save();
            _canvas.Translate(0, top);
            _canvas.DrawPicture(picture);
            _canvas.Restore();
            return top + bottom;
        }

        private float Video(BiliVideo video, float left, float width, float top)
        {
            float coverHeight = width * COVER_RATIO;
            SKRect cover = new(left, top, left + width, top + coverHeight);
            Image(video.Cover.Length > 0 ? Sized(video.Cover, FULL_PIXELS, square: false) : null, cover, topCrop: false);
            if (video.Duration.Length > 0)
            {
                float badgeWidth = _fonts.Painter.Measure(video.Duration, _fonts.LatinBold, BADGE_SIZE) + BADGE_PAD * 2;
                SKRect badge = new(cover.Right - BADGE_MARGIN - badgeWidth, cover.Bottom - BADGE_MARGIN - BADGE_HEIGHT, cover.Right - BADGE_MARGIN, cover.Bottom - BADGE_MARGIN);
                Fill(badge, CardPalette.InkStrong);
                Text(video.Duration, _fonts.LatinBold, BADGE_SIZE, CardPalette.Signal, badge.Left + BADGE_PAD, badge.MidY + BADGE_SIZE * 0.36f);
            }

            float y = Paragraph(video.Title, _fonts.Bold, VIDEO_TITLE, CardPalette.Ink, left, width, cover.Bottom + GAP, DESC_LINES);
            if (video.Description.Length > 0)
                y = Paragraph(video.Description, _fonts.Regular, VIDEO_DESC, CardPalette.InkSecondary, left, width, y + GAP / 2, DESC_LINES);
            if (video.Bvid.Length == 0) return y;

            y += GAP;
            Rule(left, left + width, y);
            float baseline = Baseline(y + GAP / 2, BODY_SIZE);
            Text("BV", _fonts.Latin, LABEL_SIZE, CardPalette.InkTertiary, left, baseline);
            Text(video.Bvid, _fonts.Latin, ORIGIN_SIZE, CardPalette.Ink, left + LABEL_WIDTH, baseline);
            return y + GAP / 2 + BODY_SIZE * LEADING;
        }

        // 单张图按宽度完整缩放（高度最多为宽度的 2 倍，超出取顶部）；多张为正方形网格，2 与 4 张两列、其余三列，最多 9 格。
        private float Grid(IReadOnlyList<BiliPicture> pictures, float left, float width, float top)
        {
            if (pictures.Count == 1)
            {
                string url = Sized(pictures[0].Url, FULL_PIXELS, square: false);
                SKImage? image = _images.GetValueOrDefault(url);
                (float w, float h) = image is not null ? (image.Width, image.Height) : (pictures[0].Width, pictures[0].Height);
                float height = w > 0 && h > 0 ? Math.Min(width * h / w, width * SINGLE_MAX) : width * COVER_RATIO;
                Image(url, new SKRect(left, top, left + width, top + height), topCrop: true);
                return top + height;
            }

            int columns = pictures.Count is 2 or 4 ? 2 : 3;
            int shown = Math.Min(pictures.Count, GRID_MAX);
            float cell = (width - GRID_GAP * (columns - 1)) / columns;
            for (int index = 0; index < shown; index++)
            {
                float x = left + index % columns * (cell + GRID_GAP);
                float y = top + index / columns * (cell + GRID_GAP);
                SKRect rect = new(x, y, x + cell, y + cell);
                Image(Sized(pictures[index].Url, THUMB_PIXELS, square: false), rect, topCrop: true);
                if (index == GRID_MAX - 1 && pictures.Count > GRID_MAX)
                {
                    Fill(rect, CardPalette.InkStrong.WithAlpha(OVERLAY_ALPHA));
                    string more = "+" + (pictures.Count - GRID_MAX).ToString(CultureInfo.InvariantCulture);
                    float textWidth = _fonts.Painter.Measure(more, _fonts.LatinBold, OVERLAY_SIZE);
                    Text(more, _fonts.LatinBold, OVERLAY_SIZE, CardPalette.Paper, rect.MidX - textWidth / 2, rect.MidY + OVERLAY_SIZE * 0.36f);
                }
            }

            int rows = (shown + columns - 1) / columns;
            return top + rows * cell + (rows - 1) * GRID_GAP;
        }

        // 铺满目标区域：比目标更高的图取顶部（长图），更宽的图取水平居中。
        private void Image(string? url, SKRect target, bool topCrop)
        {
            if (url is null || _images.GetValueOrDefault(url) is not { } image)
            {
                Fill(target, CardPalette.Muted);
                float textWidth = _fonts.Painter.Measure(IMAGE_FAILED, _fonts.Regular, LABEL_SIZE);
                Text(IMAGE_FAILED, _fonts.Regular, LABEL_SIZE, CardPalette.InkTertiary, target.MidX - textWidth / 2, target.MidY + LABEL_SIZE * 0.36f);
                return;
            }

            float scale = Math.Max(target.Width / image.Width, target.Height / image.Height);
            float sourceWidth = target.Width / scale;
            float sourceHeight = target.Height / scale;
            float x = (image.Width - sourceWidth) / 2;
            float y = topCrop ? 0 : (image.Height - sourceHeight) / 2;
            _canvas.DrawImage(image, new SKRect(x, y, x + sourceWidth, y + sourceHeight), target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }

        // 富文本：逐字素测量并换行（空白与汉字等之后可断开，单词中间溢出时整词移到下一行），按片段类型着色。
        private float Rich(IReadOnlyList<BiliText> text, float left, float width, float top, float size)
        {
            List<List<Glyph>> lines = [[]];
            float lineWidth = 0;
            int lastBreak = 0;
            foreach (Glyph glyph in Glyphs(text, size))
            {
                List<Glyph> line = lines[^1];
                if (glyph.Element == NEWLINE)
                {
                    lines.Add([]);
                    lineWidth = 0;
                    lastBreak = 0;
                    continue;
                }

                if (lineWidth + glyph.Width > width && line.Count > 0)
                {
                    List<Glyph> carried = lastBreak > 0 && lastBreak < line.Count ? line[lastBreak..] : [];
                    line.RemoveRange(line.Count - carried.Count, carried.Count);
                    line = [.. carried.SkipWhile(item => string.IsNullOrWhiteSpace(item.Element))];
                    lines.Add(line);
                    lineWidth = line.Sum(item => item.Width);
                    lastBreak = 0;
                    if (string.IsNullOrWhiteSpace(glyph.Element) && line.Count == 0) continue;
                }

                line.Add(glyph);
                lineWidth += glyph.Width;
                if (IsBreakAfter(glyph.Element)) lastBreak = line.Count;
            }

            // 正文末尾的换行（如摘要以 \n 结尾）不占行。
            while (lines.Count > 1 && lines[^1].All(item => string.IsNullOrWhiteSpace(item.Element))) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > MAX_TEXT_LINES) lines = lines[..MAX_TEXT_LINES];
            float lineHeight = size * LEADING;
            for (int index = 0; index < lines.Count; index++)
            {
                float x = left;
                float baseline = Baseline(top + index * lineHeight, size);
                foreach ((SKColor color, string run) in Runs(lines[index]))
                {
                    Text(run, _fonts.Regular, size, color, x, baseline);
                    x += _fonts.Painter.Measure(run, _fonts.Regular, size);
                }
            }

            return top + lines.Count * lineHeight;
        }

        private IEnumerable<Glyph> Glyphs(IReadOnlyList<BiliText> text, float size)
        {
            foreach (BiliText span in text)
            {
                SKColor color = span.Kind switch
                {
                    BiliTextKind.Highlight => CardPalette.SignalInk,
                    BiliTextKind.Emoji     => CardPalette.InkSecondary,
                    _                      => CardPalette.Ink
                };
                TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(span.Text.Replace(CRLF, NEWLINE, StringComparison.Ordinal));
                while (enumerator.MoveNext())
                {
                    string element = enumerator.GetTextElement();
                    yield return new Glyph
                    {
                        Element = element,
                        Color   = color,
                        Width   = element == NEWLINE ? 0 :_fonts.Painter.Measure(element, _fonts.Regular, size)
                    };
                }
            }
        }

        // 同色的相邻字素合为一段绘制，整形与字体回退以段为单位。
        private static IEnumerable<(SKColor Color, string Text)> Runs(List<Glyph> line)
        {
            int start = 0;
            while (start < line.Count)
            {
                int end = start;
                while (end < line.Count && line[end].Color == line[start].Color) end++;
                yield return (line[start].Color, string.Concat(line.Skip(start).Take(end - start).Select(item => item.Element)));
                start = end;
            }
        }

        private static bool IsBreakAfter(string element) =>
            string.IsNullOrWhiteSpace(element) || !element.All(character => char.IsAsciiLetterOrDigit(character) || char.IsPunctuation(character));

        private float Paragraph(string text, SKTypeface typeface, float size, SKColor color, float left, float width, float top, int maxLines)
        {
            IReadOnlyList<string> lines = _fonts.Painter.Wrap(text, typeface, size, width, maxLines);
            for (int index = 0; index < lines.Count; index++)
                Text(lines[index], typeface, size, color, left, Baseline(top + index * size * LEADING, size));
            return top + lines.Count * size * LEADING;
        }

        private void Text(string text, SKTypeface typeface, float size, SKColor color, float x, float baseline)
        {
            using SKPaint paint = new()
            {
                Color       = color,
                IsAntialias = true
            };
            _fonts.Painter.Draw(_canvas, text, typeface, size, x, baseline, paint);
        }

        private void Fill(SKRect rect, SKColor color)
        {
            using SKPaint paint = new()
            {
                Color       = color,
                IsAntialias = true
            };
            _canvas.DrawRect(rect, paint);
        }

        private void Rule(float left, float right, float y) => Fill(new SKRect(left, y, right, y + 1), CardPalette.Line);

        // 与宿主卡片相同的基线：在高 size × LEADING 的行框中垂直居中。
        private static float Baseline(float top, float size) => top + (size * LEADING - size) / 2 + size * 0.88f;
    }

    /// <summary>富文本中的一个字素及其颜色与宽度。</summary>
    private readonly struct Glyph
    {
        public required string Element { get; init; }

        public required SKColor Color { get; init; }

        public required float Width { get; init; }
    }
}
