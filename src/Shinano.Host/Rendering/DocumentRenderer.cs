using System.Globalization;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Shinano.Framework.Rendering;
using SkiaSharp;

namespace Shinano.Host.Rendering;

/// <summary>
/// 文档卡片的一次绘制：顶边墨条与黄色斜楔，Markdown 正文放在一块白色面板中，下方为页脚。
/// 每次绘制新建实例，单独发送的图片按出现顺序追加到调用方给出的列表。
/// </summary>
internal sealed class DocumentRenderer
{
    private const float  TOP_BAR          = 8;
    private const float  PANEL_PADDING_X  = 28;
    private const float  PANEL_PADDING_Y  = 22;
    private const float  PANEL_GAP        = 20;
    private const float  BODY_SIZE        = 17;
    private const float  BODY_LEADING     = 1.75f;
    private const float  HEADING_LEADING  = 1.4f;
    private const float  H1_SIZE          = 26;
    private const float  H2_SIZE          = 21;
    private const float  H3_SIZE          = 18;
    private const float  H1_RULE          = 2;
    private const float  H1_RULE_GAP      = 10;
    private const float  H2_DIAMOND       = 11;
    private const float  H2_DIAMOND_STROKE = 1.5f;
    private const float  H2_INDENT        = 24;
    private const float  BLOCK_GAP        = 14;
    private const float  HEADING_GAP      = 24;
    private const float  LIST_INDENT      = 26;
    private const float  ITEM_GAP_TIGHT   = 6;
    private const float  ITEM_GAP_LOOSE   = 10;
    private const float  BULLET_SIZE      = 7;
    private const float  BULLET_OFFSET    = 6;
    private const float  BULLET_STROKE    = 1.5f;
    private const float  CHECKBOX_SIZE    = 13;
    private const float  CHECK_STROKE     = 2;
    private const float  NUMBER_SIZE      = 13;
    private const string NUMBER_FORMAT    = "00";
    private const float  QUOTE_BAR        = 4;
    private const float  QUOTE_PADDING_X  = 16;
    private const float  QUOTE_PADDING_Y  = 10;
    private const float  CODE_SIZE          = 14;
    private const float  CODE_LEADING       = 1.65f;
    private const float  CODE_PADDING_X     = 18;
    private const float  CODE_PADDING_Y     = 14;
    private const float  CODE_HEADER_HEIGHT = 30;
    private const float  CODE_ACCENT_WIDTH  = 3;
    private const float  CODE_DIAMOND_SIZE  = 6;
    private const float  CODE_LABEL_SIZE    = 11;
    private const float  CODE_BORDER        = 1;
    private const float  MATH_BLOCK_SIZE    = 20;
    private const float  MATH_INLINE_SIZE   = 18;
    private const float  MATH_PADDING_X     = 16;
    private const float  MATH_PADDING_Y     = 12;
    private const float  TABLE_SIZE       = 16;
    private const float  TABLE_LEADING    = 1.5f;
    private const float  TABLE_PADDING_X  = 12;
    private const float  TABLE_PADDING_Y  = 8;
    private const float  TABLE_MIN_COLUMN = 48;
    private const float  TABLE_HEADER_RULE = 2;
    private const float  HATCH_HEIGHT     = 6;
    private const float  HATCH_SPACING    = 6;
    private const float  HATCH_STROKE     = 1.5f;
    private const float  IMAGE_BORDER     = 1;
    private const float  CAPTION_SIZE     = 14;
    private const float  CAPTION_GAP      = 6;
    private const string PLACEHOLDER      = "图 {0}";

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
                                                        .UsePipeTables()
                                                        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
                                                        .UseTaskLists()
                                                        .UseMathematics()
                                                        .DisableHtml()
                                                        .Build();

    private readonly DocumentCard                   _card;
    private readonly List<byte[]>                   _attachments;
    private readonly float                          _scale;
    private readonly Dictionary<string, SKImage?>   _images   = [];
    private readonly Dictionary<string, int>        _numbers  = [];
    private readonly List<RenderedMathFormula>      _formulas = [];
    private          int                            _quoteDepth;

    /// <summary>准备一次绘制。</summary>
    /// <param name="card">文档。</param>
    /// <param name="attachments">单独发送的图片按「图 n」顺序追加到此列表。</param>
    /// <param name="scale">输出像素与逻辑像素之比，用于判断内嵌图片是否需要缩小。</param>
    public DocumentRenderer(DocumentCard card, List<byte[]> attachments, float scale)
    {
        _card        = card;
        _attachments = attachments;
        _scale       = scale;
    }

    private static float ContentLeft => CardCanvas.PANEL_MARGIN + PANEL_PADDING_X;

    private static float ContentRight => CardCanvas.WIDTH - CardCanvas.PANEL_MARGIN - PANEL_PADDING_X;

    /// <summary>绘制整张文档，返回卡片底边。</summary>
    public float Draw(CardCanvas canvas)
    {
        try
        {
            string normalized = LatexMathRenderer.NormalizeDelimiters(_card.Markdown);
            MarkdownDocument document = Markdown.Parse(normalized, Pipeline);
            canvas.Fill(new SKRect(0, 0, CardCanvas.WIDTH, TOP_BAR), CardPalette.Ink);
            canvas.Edge(TOP_BAR);
            float bottom = canvas.Panel(TOP_BAR, panel => Blocks(panel, document, ContentLeft, ContentRight, 0, 0), PANEL_PADDING_Y, PANEL_GAP);
            return canvas.Footer(bottom, _card.Note, _card.FooterRight);
        }
        finally
        {
            foreach (SKImage? image in _images.Values) image?.Dispose();
            foreach (RenderedMathFormula formula in _formulas) formula.Dispose();
        }
    }

    private float Blocks(CardCanvas canvas, ContainerBlock container, float left, float right, float top, int depth, float gap = BLOCK_GAP)
    {
        float y = top;
        bool first = true;
        foreach (Block block in container.Where(block => block is not LinkReferenceDefinitionGroup))
        {
            if (!first) y += block is HeadingBlock ? HEADING_GAP : gap;
            y = Block(canvas, block, left, right, y, depth);
            first = false;
        }

        return y;
    }

    private float Block(CardCanvas canvas, Block block, float left, float right, float top, int depth) =>
        block switch
        {
            HeadingBlock heading     => Heading(canvas, heading, left, right, top),
            Table table              => TableBlock(canvas, table, left, right, top),
            ParagraphBlock paragraph => Paragraph(canvas, paragraph.Inline, Body(canvas), left, right, top),
            ListBlock list           => List(canvas, list, left, right, top, depth),
            QuoteBlock quote         => Quote(canvas, quote, left, right, top, depth),
            MathBlock math           => MathBlockView(canvas, math.Lines.ToString(), left, right, top),
            CodeBlock code           => Code(canvas, code, left, right, top),
            ThematicBreakBlock       => Hatch(canvas, left, right, top),
            ContainerBlock container => Blocks(canvas, container, left, right, top, depth),
            LeafBlock leaf           => Plain(canvas, leaf.Lines.ToString(), left, right, top),
            _                        => top
        };

    private float Heading(CardCanvas canvas, HeadingBlock heading, float left, float right, float top)
    {
        float size = heading.Level switch
        {
            1 => H1_SIZE,
            2 => H2_SIZE,
            3 => H3_SIZE,
            _ => BODY_SIZE
        };
        RichTextStyle style = new()
        {
            Typeface       = canvas.Fonts.Bold,
            StrongTypeface = canvas.Fonts.Bold,
            Size           = size,
            Leading        = HEADING_LEADING,
            Color          = CardPalette.InkStrong
        };
        float textLeft = heading.Level == 2 ? left + H2_INDENT : left;
        RichTextBlock text = RichTextBlock.Layout(canvas, Spans(heading.Inline), style, right - textLeft);
        if (heading.Level == 2)
        {
            float cy = top + text.LineHeight / 2;
            float cx = left + H2_DIAMOND / 2 + 1;
            canvas.Diamond(cx, cy, H2_DIAMOND + H2_DIAMOND_STROKE * 2, CardPalette.Ink);
            canvas.Diamond(cx, cy, H2_DIAMOND, CardPalette.Signal);
        }

        float bottom = top + text.Draw(textLeft, right, top);
        if (heading.Level != 1) return bottom;
        canvas.Rule(left, right, bottom + H1_RULE_GAP, CardPalette.Ink, H1_RULE);
        return bottom + H1_RULE_GAP + H1_RULE;
    }

    // 内嵌模式下可解码的图片与块级公式单独成块，打断所在段落；其余行内元素留在段落内。
    private float Paragraph(CardCanvas canvas, ContainerInline? inline, RichTextStyle style, float left, float right, float top)
    {
        List<RichSpan> spans = [];
        float y = top;
        bool any = false;
        foreach (object segment in Segments(inline))
        {
            if (segment is RichSpan span)
            {
                spans.Add(span);
                continue;
            }

            if (segment is InlineImage image)
            {
                y = FlushText();
                if (any) y += BLOCK_GAP;
                y = Image(canvas, image, left, right, y);
                any = true;
                continue;
            }

            if (segment is InlineMathBlock math)
            {
                y = FlushText();
                if (any) y += BLOCK_GAP;
                y = MathBlockView(canvas, math.LaTeX, left, right, y);
                any = true;
            }
        }

        return FlushText();

        float FlushText()
        {
            if (spans.Count == 0 || spans.All(item => item.Formula is null && string.IsNullOrWhiteSpace(item.Text))) return y;
            if (any) y += BLOCK_GAP;
            y += RichTextBlock.Layout(canvas, spans, style, right - left).Draw(left, right, y);
            spans.Clear();
            any = true;
            return y;
        }
    }

    private float List(CardCanvas canvas, ListBlock list, float left, float right, float top, int depth)
    {
        int number = list.IsOrdered && int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int start) ? start : 1;
        float gap = list.IsLoose ? ITEM_GAP_LOOSE : ITEM_GAP_TIGHT;
        float y = top;
        bool first = true;
        foreach (ListItemBlock item in list.OfType<ListItemBlock>())
        {
            if (!first) y += gap;
            float markerCenter = y + BODY_SIZE * BODY_LEADING / 2;
            TaskList? task = item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList checkbox } ? checkbox : null;
            if (task is not null) Checkbox(canvas, left, markerCenter, task.Checked);
            else if (list.IsOrdered)
                canvas.Text(number.ToString(NUMBER_FORMAT, CultureInfo.InvariantCulture), canvas.Fonts.Latin, NUMBER_SIZE, CardPalette.InkTertiary, left, CardCanvas.Baseline(y, BODY_SIZE, BODY_LEADING));
            else Bullet(canvas, left + BULLET_OFFSET, markerCenter, depth > 0);
            // 列表项内的段落与嵌套列表按列表项间距排列。
            y = Blocks(canvas, item, left + LIST_INDENT, right, y, depth + 1, gap);
            number++;
            first = false;
        }

        return y;
    }

    private static void Bullet(CardCanvas canvas, float left, float centerY, bool outline)
    {
        SKRect rect = new(left, centerY - BULLET_SIZE / 2, left + BULLET_SIZE, centerY + BULLET_SIZE / 2);
        if (!outline)
        {
            canvas.Fill(rect, CardPalette.Ink);
            return;
        }

        using SKPaint stroke = Stroke(CardPalette.Ink, BULLET_STROKE);
        rect.Inflate(-BULLET_STROKE / 2, -BULLET_STROKE / 2);
        canvas.Canvas.DrawRect(rect, stroke);
    }

    private static void Checkbox(CardCanvas canvas, float left, float centerY, bool done)
    {
        SKRect rect = new(left, centerY - CHECKBOX_SIZE / 2, left + CHECKBOX_SIZE, centerY + CHECKBOX_SIZE / 2);
        if (done)
        {
            canvas.Fill(rect, CardPalette.Ink);
            using SKPaint check = Stroke(CardPalette.Signal, CHECK_STROKE);
            using SKPathBuilder builder = new();
            builder.MoveTo(rect.Left + CHECKBOX_SIZE * 0.22f, rect.Top + CHECKBOX_SIZE * 0.52f);
            builder.LineTo(rect.Left + CHECKBOX_SIZE * 0.42f, rect.Top + CHECKBOX_SIZE * 0.72f);
            builder.LineTo(rect.Left + CHECKBOX_SIZE * 0.78f, rect.Top + CHECKBOX_SIZE * 0.3f);
            using SKPath path = builder.Detach();
            canvas.Canvas.DrawPath(path, check);
            return;
        }

        using SKPaint stroke = Stroke(CardPalette.Ink, BULLET_STROKE);
        rect.Inflate(-BULLET_STROKE / 2, -BULLET_STROKE / 2);
        canvas.Canvas.DrawRect(rect, stroke);
    }

    // 引用先录制内容以得到高度，再铺凹陷底色与左侧信号黄竖条。
    private float Quote(CardCanvas canvas, QuoteBlock quote, float left, float right, float top, int depth)
    {
        _quoteDepth++;
        (SKPicture picture, float height) = CardCanvas.Record(
            canvas.Fonts,
            inner => Blocks(inner, quote, left + QUOTE_BAR + QUOTE_PADDING_X, right - QUOTE_PADDING_X, QUOTE_PADDING_Y, depth) + QUOTE_PADDING_Y);
        _quoteDepth--;
        using (picture)
        {
            canvas.Fill(new SKRect(left, top, right, top + height), CardPalette.Sunken);
            canvas.Fill(new SKRect(left, top, left + QUOTE_BAR, top + height), CardPalette.Signal);
            canvas.Canvas.Save();
            canvas.Canvas.Translate(0, top);
            canvas.Canvas.DrawPicture(picture);
            canvas.Canvas.Restore();
        }

        return top + height;
    }

    // 终末地暗色代码块（参考 endfield-ui-skill）：冷暗灰底、深色标头与信号黄强调条，正文逐段着色并保留换行颜色。
    private static float Code(CardCanvas canvas, CodeBlock code, float left, float right, float top)
    {
        string rawLanguage = code is FencedCodeBlock { Info: { Length: > 0 } info } ? info.Trim() : string.Empty;
        string displayLanguage = FormatCodeLanguage(rawLanguage);
        float textLeft = left + CODE_PADDING_X;
        float textRight = right - CODE_PADDING_X;
        float maxWidth = Math.Max(1, textRight - textLeft);

        IReadOnlyList<IReadOnlyList<CodeToken>> highlighted = CodeHighlighter.Highlight(code.Lines.ToString(), rawLanguage);
        List<List<CodeToken>> wrappedLines = [];
        foreach (IReadOnlyList<CodeToken> line in highlighted)
            wrappedLines.AddRange(WrapCodeLine(canvas, line, maxWidth));
        if (wrappedLines.Count == 0) wrappedLines.Add([]);

        float lineHeight = CODE_SIZE * CODE_LEADING;
        float bodyHeight = CODE_PADDING_Y * 2 + wrappedLines.Count * lineHeight;
        float totalHeight = CODE_HEADER_HEIGHT + bodyHeight;
        SKRect cardRect = new(left, top, right, top + totalHeight);
        SKRect headerRect = new(left, top, right, top + CODE_HEADER_HEIGHT);

        canvas.Fill(cardRect, CodeHighlighter.Background);
        canvas.Fill(headerRect, CodeHighlighter.HeaderBackground);
        canvas.Fill(new SKRect(left, top, left + CODE_ACCENT_WIDTH, top + CODE_HEADER_HEIGHT), CardPalette.Signal);
        canvas.Rule(left, right, top + CODE_HEADER_HEIGHT, CodeHighlighter.Border, CODE_BORDER);
        using (SKPaint border = Stroke(CodeHighlighter.Border, CODE_BORDER))
            canvas.Canvas.DrawRect(cardRect, border);

        float headerCenterY = top + CODE_HEADER_HEIGHT / 2;
        float labelBaseline = CardCanvas.Baseline(top + (CODE_HEADER_HEIGHT - CODE_LABEL_SIZE * TABLE_LEADING) / 2, CODE_LABEL_SIZE, TABLE_LEADING);
        canvas.Diamond(textLeft + CODE_DIAMOND_SIZE / 2, headerCenterY, CODE_DIAMOND_SIZE, CardPalette.Signal);
        canvas.Text(displayLanguage, canvas.Fonts.Latin, CODE_LABEL_SIZE, CardPalette.Signal, textLeft + CODE_DIAMOND_SIZE + 8, labelBaseline);
        string lineBadge = string.Format(CultureInfo.InvariantCulture, "{0} {1}", highlighted.Count, highlighted.Count == 1 ? "LINE" : "LINES");
        canvas.TextRight(lineBadge, canvas.Fonts.Latin, CODE_LABEL_SIZE, CardPalette.InkTertiary, textRight, labelBaseline);

        float y = top + CODE_HEADER_HEIGHT + CODE_PADDING_Y;
        foreach (List<CodeToken> line in wrappedLines)
        {
            float x = textLeft;
            float baseline = CardCanvas.Baseline(y, CODE_SIZE, CODE_LEADING);
            foreach (CodeToken segment in line)
            {
                if (segment.Text.Length == 0) continue;
                canvas.Text(segment.Text, canvas.Fonts.Mono, CODE_SIZE, segment.Color, x, baseline);
                x += canvas.Painter.Measure(segment.Text, canvas.Fonts.Mono, CODE_SIZE);
            }

            y += lineHeight;
        }

        return top + totalHeight;
    }

    private static string FormatCodeLanguage(string rawLanguage)
    {
        if (rawLanguage.Length == 0) return "CODE";
        int end = rawLanguage.IndexOfAny([' ', '\t', '{', ',']);
        string token = end > 0 ? rawLanguage[..end] : rawLanguage;
        return token.Length > 0 ? token.ToUpperInvariant() : "CODE";
    }

    private static List<List<CodeToken>> WrapCodeLine(CardCanvas canvas, IReadOnlyList<CodeToken> tokens, float maxWidth)
    {
        if (tokens.Count == 0) return [[]];

        List<List<CodeToken>> lines = [];
        List<CodeToken> current = [];
        float width = 0;

        foreach (CodeToken token in tokens)
        {
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(token.Text);
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                float elementWidth = canvas.Painter.Measure(element, canvas.Fonts.Mono, CODE_SIZE);
                if (current.Count > 0 && width + elementWidth > maxWidth)
                {
                    lines.Add(current);
                    current = [];
                    width = 0;
                }

                if (current.Count > 0 && current[^1].Color == token.Color)
                {
                    current[^1] = new CodeToken
                    {
                        Text  = current[^1].Text + element,
                        Color = token.Color
                    };
                }
                else
                {
                    current.Add(new CodeToken
                    {
                        Text  = element,
                        Color = token.Color
                    });
                }

                width += elementWidth;
            }
        }

        lines.Add(current);
        return lines;
    }

    private float MathBlockView(CardCanvas canvas, string latex, float left, float right, float top)
    {
        string trimmed = latex.Trim();
        if (trimmed.Length == 0) return top;

        RenderedMathFormula? formula = LatexMathRenderer.TryRender(trimmed, MATH_BLOCK_SIZE, BodyColor, displayStyle: true);
        if (formula is null) return Plain(canvas, trimmed, left, right, top);

        _formulas.Add(formula);
        float maxWidth = Math.Max(1, right - left - MATH_PADDING_X * 2);
        float scale = formula.Width > maxWidth ? maxWidth / formula.Width : 1f;
        float drawWidth = formula.Width * scale;
        float drawHeight = formula.Height * scale;
        float boxHeight = drawHeight + MATH_PADDING_Y * 2;
        SKRect rect = new(left, top, right, top + boxHeight);
        canvas.Fill(rect, CardPalette.Sunken);
        using (SKPaint border = Stroke(CardPalette.Line, IMAGE_BORDER))
            canvas.Canvas.DrawRect(rect, border);

        float drawX = left + (right - left - drawWidth) / 2;
        float drawY = top + MATH_PADDING_Y;
        canvas.Canvas.Save();
        canvas.Canvas.Translate(drawX, drawY);
        if (MathF.Abs(scale - 1f) > 0.001f)
            canvas.Canvas.Scale(scale);
        canvas.Canvas.DrawPicture(formula.Picture);
        canvas.Canvas.Restore();
        return top + boxHeight;
    }

    private static float Hatch(CardCanvas canvas, float left, float right, float top)
    {
        float y = top + BLOCK_GAP / 2;
        canvas.Canvas.Save();
        canvas.Canvas.ClipRect(new SKRect(left, y, right, y + HATCH_HEIGHT));
        using SKPaint stroke = Stroke(CardPalette.Hatch, HATCH_STROKE);
        for (float x = left - HATCH_HEIGHT; x < right; x += HATCH_SPACING)
            canvas.Canvas.DrawLine(x, y + HATCH_HEIGHT, x + HATCH_HEIGHT, y, stroke);
        canvas.Canvas.Restore();
        return y + HATCH_HEIGHT + BLOCK_GAP / 2;
    }

    private float Plain(CardCanvas canvas, string text, float left, float right, float top) =>
        top + RichTextBlock.Layout(canvas, [new RichSpan { Text = text.TrimEnd() }], Body(canvas), right - left).Draw(left, right, top);

    // 表格：列宽按各列内容的自然宽度分配，放不下时按比例压缩；表头凹陷底色加墨色粗底线，数字单元格用英文字体。
    private float TableBlock(CardCanvas canvas, Table table, float left, float right, float top)
    {
        List<TableRow> rows = [.. table.OfType<TableRow>()];
        int columns = Math.Max(table.ColumnDefinitions.Count, rows.Count == 0 ? 0 : rows.Max(row => row.Count));
        if (columns == 0) return top;

        List<List<RichSpan>[]> cells = [.. rows.Select(row => Enumerable.Range(0, columns).Select(index => CellSpans(row, index)).ToArray())];
        bool[] numeric = [.. Enumerable.Range(0, columns).Select(index => rows.Where(row => !row.IsHeader).All(row => IsNumeric(cells[rows.IndexOf(row)][index])))];
        float[] natural = new float[columns];
        for (int row = 0; row < rows.Count; row++)
            for (int column = 0; column < columns; column++)
            {
                RichTextBlock block = RichTextBlock.Layout(canvas, cells[row][column], CellStyle(canvas, rows[row].IsHeader, numeric[column]), float.MaxValue);
                natural[column] = Math.Max(natural[column], Math.Max(block.Width, TABLE_MIN_COLUMN) + TABLE_PADDING_X * 2);
            }

        float available = right - left;
        float total = natural.Sum();
        float[] widths = [.. natural.Select(width => width * available / total)];

        float y = top;
        for (int row = 0; row < rows.Count; row++)
        {
            bool header = rows[row].IsHeader;
            RichTextBlock[] blocks = new RichTextBlock[columns];
            for (int column = 0; column < columns; column++)
                blocks[column] = RichTextBlock.Layout(canvas, cells[row][column], CellStyle(canvas, header, numeric[column]), widths[column] - TABLE_PADDING_X * 2);
            float height = blocks.Max(block => Math.Max(block.Height, TABLE_SIZE * TABLE_LEADING)) + TABLE_PADDING_Y * 2;
            if (header) canvas.Fill(new SKRect(left, y, right, y + height), CardPalette.Sunken);
            float x = left;
            for (int column = 0; column < columns; column++)
            {
                LineAlignment alignment = Alignment(table, column, numeric[column] && !header);
                blocks[column].Draw(x + TABLE_PADDING_X, x + widths[column] - TABLE_PADDING_X, y + TABLE_PADDING_Y, alignment);
                x += widths[column];
            }

            y += height;
            if (header) canvas.Rule(left, right, y - TABLE_HEADER_RULE, CardPalette.Ink, TABLE_HEADER_RULE);
            else if (row < rows.Count - 1) canvas.Rule(left, right, y - 1, CardPalette.Line);
        }

        return y;
    }

    private static LineAlignment Alignment(Table table, int column, bool numeric) =>
        (column < table.ColumnDefinitions.Count ? table.ColumnDefinitions[column].Alignment : null) switch
        {
            TableColumnAlign.Right  => LineAlignment.Right,
            TableColumnAlign.Center => LineAlignment.Center,
            TableColumnAlign.Left   => LineAlignment.Left,
            _                       => numeric ? LineAlignment.Right : LineAlignment.Left
        };

    private List<RichSpan> CellSpans(TableRow row, int column)
    {
        TableCell? cell = row.OfType<TableCell>().FirstOrDefault(item => item.ColumnIndex == column)
                          ?? (column < row.Count ? row[column] as TableCell : null);
        return cell?.FirstOrDefault() is ParagraphBlock paragraph ? Spans(paragraph.Inline) : [];
    }

    private static bool IsNumeric(List<RichSpan> spans)
    {
        string text = string.Concat(spans.Select(span => span.Text)).Trim();
        return text.Length > 0 && text.All(character => char.IsAsciiDigit(character) || character is '.' or ',' or '%' or '-' or '+');
    }

    private RichTextStyle CellStyle(CardCanvas canvas, bool header, bool numeric) =>
        new()
        {
            Typeface       = header ? canvas.Fonts.Bold : numeric ? canvas.Fonts.Latin : canvas.Fonts.Regular,
            StrongTypeface = canvas.Fonts.Bold,
            Size           = TABLE_SIZE,
            Leading        = TABLE_LEADING,
            Color          = BodyColor
        };

    private float Image(CardCanvas canvas, InlineImage reference, float left, float right, float top)
    {
        SKImage image = reference.Image;
        float width = Math.Min(right - left, image.Width / _scale);
        float height = width * image.Height / image.Width;
        SKRect rect = new(left, top, left + width, top + height);
        canvas.Canvas.DrawImage(image, rect, new SKSamplingOptions(SKCubicResampler.Mitchell));
        using (SKPaint border = Stroke(CardPalette.Line, IMAGE_BORDER)) canvas.Canvas.DrawRect(rect, border);
        float bottom = rect.Bottom;
        if (string.IsNullOrWhiteSpace(reference.Caption)) return bottom;
        return bottom + CAPTION_GAP + canvas.Paragraph(reference.Caption, canvas.Fonts.Regular, CAPTION_SIZE, TABLE_LEADING, CardPalette.InkSecondary, left, right, bottom + CAPTION_GAP);
    }

    private RichTextStyle Body(CardCanvas canvas) =>
        new()
        {
            Typeface       = canvas.Fonts.Regular,
            StrongTypeface = canvas.Fonts.Bold,
            Size           = BODY_SIZE,
            Leading        = BODY_LEADING,
            Color          = BodyColor
        };

    private SKColor BodyColor => _quoteDepth > 0 ? CardPalette.InkSecondary : CardPalette.Ink;

    private List<RichSpan> Spans(ContainerInline? inline) => [.. Segments(inline).Select(AsInlineSpan).OfType<RichSpan>()];

    // 标题与表格单元格只能排成文字行：块级公式就地按行内公式排版，图片不显示。
    private RichSpan? AsInlineSpan(object segment) =>
        segment switch
        {
            RichSpan span        => span,
            InlineMathBlock math => FormulaSpan(math.LaTeX, math.Style, displayStyle: true),
            _                    => null
        };

    // 行内元素展开为样式文字段；内嵌模式下可解码的图片作为 InlineImage 返回。
    private IEnumerable<object> Segments(ContainerInline? inline, SpanStyle style = SpanStyle.None)
    {
        if (inline is null) yield break;
        foreach (Inline child in inline)
            foreach (object segment in Segment(child, style))
                yield return segment;
    }

    private IEnumerable<object> Segment(Inline inline, SpanStyle style)
    {
        switch (inline)
        {
            case LiteralInline literal:
                yield return new RichSpan { Text = literal.Content.ToString(), Style = style };
                break;
            case CodeInline code:
                yield return new RichSpan { Text = code.Content, Style = style | SpanStyle.Code };
                break;
            case MathInline math:
                foreach (object segment in MathSegments(math, style)) yield return segment;
                break;
            case HtmlEntityInline entity:
                yield return new RichSpan { Text = entity.Transcoded.ToString(), Style = style };
                break;
            case LineBreakInline lineBreak:
                yield return new RichSpan { Text = lineBreak.IsHard ? "\n" : SoftBreak(inline), Style = style };
                break;
            case AutolinkInline autolink:
                yield return new RichSpan { Text = autolink.Url, Style = style | SpanStyle.Link };
                break;
            case LinkInline { IsImage: true } image:
                foreach (object segment in ImageSegments(image, style)) yield return segment;
                break;
            case LinkInline link:
                foreach (object segment in Segments(link, style | SpanStyle.Link)) yield return segment;
                if (_card.ShowLinkUrls && !string.IsNullOrWhiteSpace(link.Url) && PlainText(link) != link.Url)
                    yield return new RichSpan { Text = " " + link.Url, Style = (style & ~SpanStyle.Link) | SpanStyle.Secondary };
                break;
            case EmphasisInline emphasis:
                foreach (object segment in Segments(emphasis, style | EmphasisStyle(emphasis))) yield return segment;
                break;
            case TaskList:
                break;
            case ContainerInline container:
                foreach (object segment in Segments(container, style)) yield return segment;
                break;
        }
    }

    private IEnumerable<object> ImageSegments(LinkInline image, SpanStyle style)
    {
        string caption = PlainText(image);
        SKImage? decoded = image.Url is { } key ? Decode(key) : null;
        if (decoded is null || image.Url is null)
        {
            if (caption.Length > 0) yield return new RichSpan { Text = caption, Style = style | SpanStyle.Secondary };
            yield break;
        }

        if (_card.ImageMode == DocumentImageMode.Inline)
        {
            yield return new InlineImage
            {
                Image   = decoded,
                Caption = caption
            };
            yield break;
        }

        if (!_numbers.TryGetValue(image.Url, out int number))
        {
            _attachments.Add(_card.Images[image.Url]);
            number = _attachments.Count;
            _numbers[image.Url] = number;
        }

        yield return new RichSpan { Text = string.Format(CultureInfo.InvariantCulture, PLACEHOLDER, number), Style = SpanStyle.Placeholder };
        if (caption.Length > 0) yield return new RichSpan { Text = " " + caption, Style = style | SpanStyle.Secondary };
    }

    // 只接受能解码的图片；同一键名只解码一次。
    private SKImage? Decode(string key)
    {
        if (_images.TryGetValue(key, out SKImage? cached)) return cached;
        SKImage? image = null;
        if (_card.Images.TryGetValue(key, out byte[]? bytes) && bytes.Length > 0)
        {
            using SKData data = SKData.CreateCopy(bytes);
            using SKCodec? codec = SKCodec.Create(data);
            if (codec is not null) image = SKImage.FromEncodedData(data);
        }

        _images[key] = image;
        return image;
    }

    private static SpanStyle EmphasisStyle(EmphasisInline emphasis) =>
        emphasis switch
        {
            { DelimiterChar: '~' }       => SpanStyle.Strike,
            { DelimiterCount: >= 2 }     => SpanStyle.Strong,
            _                            => SpanStyle.Emphasis
        };

    // 软换行在英文之间相当于空格；汉字之间不加空格。
    private static string SoftBreak(Inline inline) =>
        inline.PreviousSibling is LiteralInline { Content.Length: > 0 } previous && char.IsAscii(previous.Content.ToString()[^1]) ? " " : string.Empty;

    private static string PlainText(ContainerInline inline) =>
        string.Concat(inline.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));

    private static SKPaint Stroke(SKColor color, float width) =>
        new()
        {
            Color       = color,
            IsStroke    = true,
            StrokeWidth = width,
            IsAntialias = true
        };

    // 行内的 $$...$$ 交给段落单独成块，段落之外由 Spans 就地排版。
    private IEnumerable<object> MathSegments(MathInline math, SpanStyle style)
    {
        string latex = math.Content.ToString();
        if (math.DelimiterCount >= 2)
        {
            yield return new InlineMathBlock
            {
                LaTeX = latex,
                Style = style
            };
            yield break;
        }

        yield return FormulaSpan(latex, style, displayStyle: false);
    }

    // 排版失败时按原文显示，保留定界符。
    private RichSpan FormulaSpan(string latex, SpanStyle style, bool displayStyle)
    {
        RenderedMathFormula? formula = LatexMathRenderer.TryRender(latex, MATH_INLINE_SIZE, BodyColor, displayStyle);
        if (formula is null)
        {
            string delimiter = displayStyle ? "$$" : "$";
            return new RichSpan
            {
                Text  = delimiter + latex + delimiter,
                Style = style
            };
        }

        _formulas.Add(formula);
        return new RichSpan
        {
            Text    = latex,
            Style   = style,
            Formula = formula
        };
    }

    private sealed class InlineImage
    {
        public required SKImage Image   { get; init; }
        public required string  Caption { get; init; }
    }

    private sealed class InlineMathBlock
    {
        public required string    LaTeX { get; init; }
        public required SpanStyle Style { get; init; }
    }
}
