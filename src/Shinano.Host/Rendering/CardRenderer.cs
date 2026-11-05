using System.Globalization;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering.Models;
using Shinano.Host.Rendering.Models;
using SkiaSharp;

namespace Shinano.Host.Rendering;

/// <summary>
/// 宿主统一风格的消息图片（方案 C「反转终端」，参考《终末地》的工业编辑风）：凹陷底色加淡点阵的外框，墨色反转标题块，
/// 带取景角的白色面板，左右两段可选的页脚。按 720 宽的逻辑版面绘制，输出时整体缩放到配置宽度。线程安全：每次绘制使用独立的画布。
/// </summary>
public sealed class CardRenderer : IPluginImages
{
    private const string TIME_FORMAT       = "yyyy.MM.dd HH:mm:ss";
    private const string PAGE_FORMAT       = "00";
    private const string PERCENT_FORMAT    = "0.##";
    private const string LABEL_NOTICE      = "NOTICE";
    private const string LABEL_LIST        = "LIST";
    private const string LABEL_RANKING     = "RANKING";
    private const string LABEL_STATUS      = "STATUS";
    private const int    PNG_QUALITY       = 100;
    private const float  DOT_SPACING       = 6;
    private const float  DOT_RADIUS        = 1;
    private const float  INDEX_WIDTH       = 36;
    private const float  INDEX_GAP         = 12;
    private const float  INDEX_SIZE        = 14;
    private const float  ITEM_SIZE         = 18;
    private const float  ITEM_LEADING      = 1.45f;
    private const float  ITEM_PADDING      = 12;
    private const float  TAG_SIZE          = 12;
    private const float  TAG_PADDING_X     = 6;
    private const float  TAG_HEIGHT        = 19;
    private const float  TAG_STROKE        = 1.5f;
    private const float  TAG_GAP           = 8;
    private const int    ITEM_MAX_LINES    = 6;
    private const float  PODIUM_GAP        = 10;
    private const float  PODIUM_TOP_GAP    = 26;
    private const float  PODIUM_PADDING_X  = 18;
    private const float  PODIUM_PADDING_Y  = 16;
    private const float  PODIUM_FIRST      = 1.3f;
    private const float  PODIUM_RANK_SIZE  = 15;
    private const float  PODIUM_COUNT_SIZE = 44;
    private const float  PODIUM_NAME_SIZE  = 17;
    private const float  PODIUM_LINE       = 1.2f;
    private const float  PODIUM_COUNT_GAP  = 6;
    private const float  PODIUM_NAME_GAP   = 10;
    private const int    PODIUM_SLOTS      = 3;
    private const float  RANK_PADDING      = 11;
    private const float  RANK_SIZE         = 17;
    private const float  METER_PADDING     = 16;
    private const float  METER_LABEL_SIZE  = 14;
    private const float  METER_VALUE_SIZE  = 40;
    private const float  METER_UNIT_SIZE   = 18;
    private const float  METER_GAP         = 10;
    private const float  RULER_HEIGHT      = 22;
    private const float  RULER_TICK        = 8;
    private const float  RULER_BAR         = 8;
    private const int    RULER_DIVISIONS   = 10;
    private const float  THRESHOLD_WIDTH   = 2;
    private const float  THRESHOLD_DASH    = 4;
    private const float  STATUS_PADDING    = 14;
    private const float  STATUS_DIAMOND    = 10;
    private const float  STATUS_NAME_X     = 30;
    private const float  STATUS_VALUE_X    = 150;
    private const float  STATUS_SIZE       = 18;
    private const float  STATUS_DETAIL     = 15;
    private const float  LINE_LEADING      = 1.5f;
    private const double PERCENT_MAX       = 100;

    private readonly IPluginFonts     _fonts;
    private readonly float            _scale;
    private readonly int              _width;

    /// <summary>创建绘制器。</summary>
    /// <param name="fonts">公共字体。</param>
    /// <param name="options">输出宽度配置。</param>
    public CardRenderer(IPluginFonts fonts, RenderingOptions options)
    {
        _fonts     = fonts;
        _width     = Math.Clamp(options.Width, RenderingOptions.MIN_WIDTH, RenderingOptions.MAX_WIDTH);
        _scale     = _width / CardCanvas.WIDTH;
    }

    /// <inheritdoc />
    public byte[] RenderNotice(NoticeCard card) =>
        Render(canvas =>
        {
            float bottom = canvas.Header($"{LABEL_NOTICE} / {LevelName(card.Level)}", LevelColor(card.Level), card.Time.ToString(TIME_FORMAT, CultureInfo.InvariantCulture), card.Title, null);
            bottom = canvas.Panel(bottom, panel => Rows(panel, card.Rows, 0));
            return canvas.Footer(bottom, card.Code, card.FooterRight);
        });

    /// <inheritdoc />
    public byte[] RenderList(ListCard card) =>
        Render(canvas =>
        {
            string pages = $"{card.Page.ToString(PAGE_FORMAT, CultureInfo.InvariantCulture)} / {card.Pages.ToString(PAGE_FORMAT, CultureInfo.InvariantCulture)}";
            float bottom = canvas.Header(LABEL_LIST, CardPalette.Signal, pages, card.Title, card.Subtitle);
            bottom = canvas.Panel(bottom, panel => Items(panel, card.Items));
            return canvas.Footer(bottom, card.Hint, card.FooterRight);
        });

    /// <inheritdoc />
    public byte[] RenderRanking(RankingCard card) =>
        Render(canvas =>
        {
            float bottom = canvas.Header(LABEL_RANKING, CardPalette.Signal, $"TOP {card.Entries.Count}", card.Title, card.Subtitle);
            if (card.Entries.Count > 0) bottom = Podium(canvas, bottom, card.Entries);
            if (card.Entries.Count > PODIUM_SLOTS) bottom = canvas.Panel(bottom, panel => Ranks(panel, card.Entries));
            return canvas.Footer(bottom, null, card.FooterRight);
        });

    /// <inheritdoc />
    public byte[] RenderInfo(InfoCard card) =>
        Render(canvas =>
        {
            SKColor labelColor = card.Level == CardLevel.Normal ? CardPalette.Signal : LevelColor(card.Level);
            float bottom = canvas.Header(card.Label, labelColor, card.Meta, card.Title, null);
            if (card.Similarity is not null || card.Rows.Count > 0)
                bottom = canvas.Panel(bottom, panel =>
                {
                    float y = card.Similarity is { } similarity ? Meter(panel, similarity, card.Threshold, card.Level, card.Rows.Count > 0) : 0;
                    return Rows(panel, card.Rows, y);
                });
            return canvas.Footer(bottom, card.Note, card.FooterRight);
        });

    /// <inheritdoc />
    public byte[] RenderStatus(StatusCard card) =>
        Render(canvas =>
        {
            float bottom = canvas.Header(LABEL_STATUS, CardPalette.Signal, card.Time.ToString(TIME_FORMAT, CultureInfo.InvariantCulture), card.Title, null);
            bottom = canvas.Panel(bottom, panel => Statuses(panel, card.Items));
            return canvas.Footer(bottom, null, card.FooterRight);
        });

    /// <inheritdoc />
    public RenderedDocument RenderDocument(DocumentCard card)
    {
        List<byte[]> attachments = [];
        byte[] image = Render(canvas => new DocumentRenderer(card, attachments, _scale).Draw(canvas));
        return new RenderedDocument
        {
            Image       = image,
            Attachments = attachments
        };
    }

    // 先在 720 宽的逻辑版面上录制以得到高度，再铺底色与点阵并按输出宽度缩放回放。
    private byte[] Render(Func<CardCanvas, float> draw)
    {
        (SKPicture picture, float height) = CardCanvas.Record(_fonts, draw);
        using (picture)
        {
            SKImageInfo info = new(_width, (int)Math.Ceiling(height * _scale), SKColorType.Rgba8888, SKAlphaType.Premul);
            using SKSurface surface = SKSurface.Create(info);
            SKCanvas canvas = surface.Canvas;
            canvas.Clear(CardPalette.Sunken);
            canvas.Scale(_scale);
            using (SKPaint dots = DotPaint()) canvas.DrawRect(0, 0, CardCanvas.WIDTH, height, dots);
            canvas.DrawPicture(picture);
            using SKImage image = surface.Snapshot();
            using SKData data = image.Encode(SKEncodedImageFormat.Png, PNG_QUALITY);
            return data.ToArray();
        }
    }

    private static SKPaint DotPaint()
    {
        using SKPictureRecorder recorder = new();
        SKCanvas tile = recorder.BeginRecording(new SKRect(0, 0, DOT_SPACING, DOT_SPACING));
        using (SKPaint dot = new() { Color = CardPalette.Dot, IsAntialias = true })
            tile.DrawCircle(DOT_SPACING / 2, DOT_SPACING / 2, DOT_RADIUS, dot);
        using SKPicture pattern = recorder.EndRecording();
        return new SKPaint { Shader = SKShader.CreatePicture(pattern, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat) };
    }

    private static float Rows(CardCanvas panel, IReadOnlyList<CardRow> rows, float top)
    {
        float y = top;
        for (int index = 0; index < rows.Count; index++)
        {
            CardRow row = rows[index];
            bool last = index == rows.Count - 1;
            y = row.Style switch
            {
                CardRowStyle.Items => panel.ItemsRow(y, row.Label, row.Items, row.ItemBorder, last),
                _                  => panel.KeyValue(y, row.Label, row.Text, last)
            };
        }

        return y;
    }

    // 有标签的条目整体右移，续行与首行对齐（悬挂缩进）。
    private static float Items(CardCanvas panel, IReadOnlyList<ListCardItem> items)
    {
        float y = 0;
        for (int index = 0; index < items.Count; index++)
        {
            ListCardItem item = items[index];
            float top = y + ITEM_PADDING;
            float baseline = CardCanvas.Baseline(top, ITEM_SIZE, ITEM_LEADING);
            panel.Text(item.Index.ToString(PAGE_FORMAT, CultureInfo.InvariantCulture), panel.Fonts.Latin, INDEX_SIZE, CardPalette.InkTertiary, CardCanvas.CONTENT_LEFT, baseline);
            float left = CardCanvas.CONTENT_LEFT + INDEX_WIDTH + INDEX_GAP;
            if (item.Tag is { } tag) left = Tag(panel, tag, left, baseline) + TAG_GAP;
            float height = panel.Paragraph(item.Text, panel.Fonts.Regular, ITEM_SIZE, ITEM_LEADING, CardPalette.Ink, left, CardCanvas.CONTENT_RIGHT, top, ITEM_MAX_LINES);
            y = top + height + ITEM_PADDING;
            if (index < items.Count - 1) panel.Rule(CardCanvas.CONTENT_LEFT, CardCanvas.CONTENT_RIGHT, y - 1, CardPalette.Line);
        }

        return y;
    }

    private static float Tag(CardCanvas panel, string tag, float left, float baseline)
    {
        float width = panel.Painter.Measure(tag, panel.Fonts.LatinBold, TAG_SIZE) + TAG_PADDING_X * 2;
        float top = baseline - ITEM_SIZE * 0.82f;
        using SKPaint stroke = new()
        {
            Color       = CardPalette.Ink,
            IsStroke    = true,
            StrokeWidth = TAG_STROKE,
            IsAntialias = true
        };
        panel.Canvas.DrawRect(new SKRect(left, top, left + width, top + TAG_HEIGHT), stroke);
        panel.Text(tag, panel.Fonts.LatinBold, TAG_SIZE, CardPalette.Ink, left + TAG_PADDING_X, top + TAG_HEIGHT / 2 + TAG_SIZE * 0.36f);
        return left + width;
    }

    // 前三名为并列的三格（第一名更宽、信号黄底），每格为名次、计数与名称。
    private static float Podium(CardCanvas canvas, float top, IReadOnlyList<RankingEntry> entries)
    {
        float left = CardCanvas.PANEL_MARGIN;
        float available = CardCanvas.WIDTH - CardCanvas.PANEL_MARGIN * 2 - PODIUM_GAP * (PODIUM_SLOTS - 1);
        float unit = available / (PODIUM_FIRST + PODIUM_SLOTS - 1);
        float cellTop = top + PODIUM_TOP_GAP;
        float height = PODIUM_PADDING_Y * 2 + PODIUM_RANK_SIZE * PODIUM_LINE + PODIUM_COUNT_GAP + PODIUM_COUNT_SIZE + PODIUM_NAME_GAP + PODIUM_NAME_SIZE * LINE_LEADING;
        for (int index = 0; index < Math.Min(PODIUM_SLOTS, entries.Count); index++)
        {
            float width = unit * (index == 0 ? PODIUM_FIRST : 1);
            bool first = index == 0;
            canvas.Fill(new SKRect(left, cellTop, left + width, cellTop + height), first ? CardPalette.Signal : CardPalette.Paper);
            float x = left + PODIUM_PADDING_X;
            float y = cellTop + PODIUM_PADDING_Y;
            canvas.Text($"NO.{index + 1}", canvas.Fonts.LatinBold, PODIUM_RANK_SIZE, first ? CardPalette.SignalInk : CardPalette.InkSecondary, x, CardCanvas.Baseline(y, PODIUM_RANK_SIZE, PODIUM_LINE));
            y += PODIUM_RANK_SIZE * PODIUM_LINE + PODIUM_COUNT_GAP;
            canvas.Text(entries[index].Count.ToString(CultureInfo.InvariantCulture), canvas.Fonts.LatinBold, PODIUM_COUNT_SIZE, CardPalette.Ink, x, y + PODIUM_COUNT_SIZE * 0.86f);
            y += PODIUM_COUNT_SIZE + PODIUM_NAME_GAP;
            string name = canvas.Painter.Wrap(entries[index].Name, canvas.Fonts.Bold, PODIUM_NAME_SIZE, width - PODIUM_PADDING_X * 2, 1)[0];
            canvas.Text(name, canvas.Fonts.Bold, PODIUM_NAME_SIZE, CardPalette.Ink, x, CardCanvas.Baseline(y, PODIUM_NAME_SIZE, LINE_LEADING));
            left += width + PODIUM_GAP;
        }

        return cellTop + height;
    }

    private static float Ranks(CardCanvas panel, IReadOnlyList<RankingEntry> entries)
    {
        float y = 0;
        for (int index = PODIUM_SLOTS; index < entries.Count; index++)
        {
            float top = y + RANK_PADDING;
            float baseline = CardCanvas.Baseline(top, RANK_SIZE, LINE_LEADING);
            panel.Text((index + 1).ToString(PAGE_FORMAT, CultureInfo.InvariantCulture), panel.Fonts.Latin, INDEX_SIZE, CardPalette.InkTertiary, CardCanvas.CONTENT_LEFT, baseline);
            string count = entries[index].Count.ToString(CultureInfo.InvariantCulture);
            float countWidth = panel.Painter.Measure(count, panel.Fonts.LatinBold, RANK_SIZE);
            float nameLeft = CardCanvas.CONTENT_LEFT + INDEX_WIDTH + INDEX_GAP;
            string name = panel.Painter.Wrap(entries[index].Name, panel.Fonts.Regular, RANK_SIZE, CardCanvas.CONTENT_RIGHT - countWidth - INDEX_GAP - nameLeft, 1)[0];
            panel.Text(name, panel.Fonts.Regular, RANK_SIZE, CardPalette.Ink, nameLeft, baseline);
            panel.TextRight(count, panel.Fonts.LatinBold, RANK_SIZE, CardPalette.Ink, CardCanvas.CONTENT_RIGHT, baseline);
            y = top + RANK_SIZE * LINE_LEADING + RANK_PADDING;
            if (index < entries.Count - 1) panel.Rule(CardCanvas.CONTENT_LEFT, CardCanvas.CONTENT_RIGHT, y - 1, CardPalette.Line);
        }

        return y;
    }

    // 相似度：左上说明，右上大号百分比，下方 10 等分刻度尺、填充条与阈值虚线。
    private static float Meter(CardCanvas panel, double similarity, double? threshold, CardLevel level, bool hasRows)
    {
        float left = CardCanvas.CONTENT_LEFT;
        float right = CardCanvas.CONTENT_RIGHT;
        float top = METER_PADDING;
        string caption = threshold is { } line ? $"相似度　阈值 {line.ToString(PERCENT_FORMAT, CultureInfo.InvariantCulture)}%" : "相似度";
        float valueBaseline = top + METER_VALUE_SIZE * 0.86f;
        panel.Text(caption, panel.Fonts.Regular, METER_LABEL_SIZE, CardPalette.InkSecondary, left, valueBaseline);
        float unitWidth = panel.Painter.Measure("%", panel.Fonts.LatinBold, METER_UNIT_SIZE);
        panel.TextRight("%", panel.Fonts.LatinBold, METER_UNIT_SIZE, CardPalette.Ink, right, valueBaseline);
        panel.TextRight(similarity.ToString(PERCENT_FORMAT, CultureInfo.InvariantCulture), panel.Fonts.LatinBold, METER_VALUE_SIZE, CardPalette.Ink, right - unitWidth, valueBaseline);

        float rulerTop = top + METER_VALUE_SIZE + METER_GAP;
        float rulerBottom = rulerTop + RULER_HEIGHT;
        float width = right - left;
        for (int tick = 0; tick < RULER_DIVISIONS; tick++)
        {
            float x = left + width * tick / RULER_DIVISIONS;
            panel.Fill(new SKRect(x, rulerBottom - RULER_TICK, x + 1, rulerBottom), CardPalette.LineStrong);
        }

        float fill = (float)(Math.Clamp(similarity, 0, PERCENT_MAX) / PERCENT_MAX) * width;
        panel.Fill(new SKRect(left, rulerBottom - RULER_TICK - RULER_BAR, left + fill, rulerBottom - RULER_TICK), level == CardLevel.Normal ? CardPalette.Ink : LevelColor(level));
        if (threshold is { } mark)
        {
            float x = left + (float)(Math.Clamp(mark, 0, PERCENT_MAX) / PERCENT_MAX) * width;
            for (float y = rulerTop; y < rulerBottom; y += THRESHOLD_DASH * 2)
                panel.Fill(new SKRect(x, y, x + THRESHOLD_WIDTH, Math.Min(y + THRESHOLD_DASH, rulerBottom)), CardPalette.Ink);
        }

        float bottom = rulerBottom + METER_PADDING;
        if (hasRows) panel.Rule(left, right, bottom - 1, CardPalette.Line);
        return bottom;
    }

    private static float Statuses(CardCanvas panel, IReadOnlyList<StatusItem> items)
    {
        float y = 0;
        for (int index = 0; index < items.Count; index++)
        {
            StatusItem item = items[index];
            float top = y + STATUS_PADDING;
            float baseline = CardCanvas.Baseline(top, STATUS_SIZE, LINE_LEADING);
            panel.Diamond(CardCanvas.CONTENT_LEFT + STATUS_DIAMOND / 2 + 2, top + STATUS_SIZE * LINE_LEADING / 2, STATUS_DIAMOND, LevelColor(item.Level));
            panel.Text(item.Name, panel.Fonts.Regular, STATUS_SIZE, CardPalette.Ink, CardCanvas.CONTENT_LEFT + STATUS_NAME_X, baseline);
            panel.Text(item.Value, panel.Fonts.Regular, STATUS_SIZE, CardPalette.Ink, CardCanvas.CONTENT_LEFT + STATUS_VALUE_X, baseline);
            if (item.Detail is { } detail) panel.TextRight(detail, panel.Fonts.Latin, STATUS_DETAIL, CardPalette.InkSecondary, CardCanvas.CONTENT_RIGHT, baseline);
            y = top + STATUS_SIZE * LINE_LEADING + STATUS_PADDING;
            if (index < items.Count - 1) panel.Rule(CardCanvas.CONTENT_LEFT, CardCanvas.CONTENT_RIGHT, y - 1, CardPalette.Line);
        }

        return y;
    }

    private static string LevelName(CardLevel level) =>
        level switch
        {
            CardLevel.Error   => "错误",
            CardLevel.Warning => "警告",
            _                 => "正常"
        };

    // 等级色：正常为绿色，用于通知（与故障通知形成对照）和状态项；信息卡正常时改用信号黄。
    private static SKColor LevelColor(CardLevel level) =>
        level switch
        {
            CardLevel.Error   => CardPalette.Error,
            CardLevel.Warning => CardPalette.Warning,
            _                 => CardPalette.Normal
        };
}
