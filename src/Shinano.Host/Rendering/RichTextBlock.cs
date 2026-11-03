using System.Globalization;
using Shinano.Host.Rendering.Models;
using SkiaSharp;

namespace Shinano.Host.Rendering;

/// <summary>
/// 混合样式的行内文字排版：把各段文字切成不可再分的词（连续的字母数字为一词，汉字、emoji 等每个字素一词，
/// 句末标点粘在前一词上），按宽度逐行排布；单词宽于整行时按字素断开。排版结果按行绘制。
/// </summary>
internal sealed class RichTextBlock
{
    private const float CODE_SIZE_RATIO      = 0.88f;
    private const float BOX_PADDING          = 6;
    private const float BOX_HEIGHT_RATIO     = 1.3f;
    private const float HIGHLIGHT_TOP        = 0.34f;
    private const float HIGHLIGHT_BOTTOM     = 0.14f;
    private const float STRIKE_OFFSET        = 0.3f;
    private const float STRIKE_WIDTH         = 1.5f;
    private const float UNDERLINE_OFFSET     = 3;
    private const float UNDERLINE_WIDTH      = 1.2f;
    private const string LINE_BREAK          = "\n";

    private readonly CardCanvas         _canvas;
    private readonly RichTextStyle      _style;
    private readonly List<List<Token>>  _lines;

    private RichTextBlock(CardCanvas canvas, RichTextStyle style, List<List<Token>> lines)
    {
        _canvas = canvas;
        _style  = style;
        _lines  = lines;
    }

    /// <summary>行高。</summary>
    public float LineHeight => _style.Size * _style.Leading;

    /// <summary>总高度；没有文字时为 0。</summary>
    public float Height => _lines.Count * LineHeight;

    /// <summary>最宽一行的宽度。</summary>
    public float Width => _lines.Count == 0 ? 0 : _lines.Max(line => line.Sum(token => token.Width));

    /// <summary>按 <paramref name="maxWidth" /> 排版。</summary>
    public static RichTextBlock Layout(CardCanvas canvas, IReadOnlyList<RichSpan> spans, RichTextStyle style, float maxWidth)
    {
        List<Token> tokens = Tokenize(canvas, spans, style, maxWidth);
        List<List<Token>> lines = [];
        List<Token> line = [];
        float width = 0;
        foreach (Token token in tokens)
        {
            if (token.IsBreak)
            {
                lines.Add(Trim(line));
                line = [];
                width = 0;
                continue;
            }

            if (token.IsSpace && line.Count == 0) continue;
            if (width + token.Width > maxWidth && line.Any(item => !item.IsSpace))
            {
                lines.Add(Trim(line));
                line = [];
                width = 0;
                if (token.IsSpace) continue;
            }

            line.Add(token);
            width += token.Width;
        }

        if (line.Count > 0) lines.Add(Trim(line));
        return new RichTextBlock(canvas, style, lines);
    }

    /// <summary>从 <paramref name="top" /> 起逐行绘制，返回所占高度。</summary>
    public float Draw(float left, float right, float top, LineAlignment alignment = LineAlignment.Left)
    {
        for (int index = 0; index < _lines.Count; index++)
        {
            List<Token> line = _lines[index];
            float lineTop = top + index * LineHeight;
            float lineWidth = line.Sum(token => token.Width);
            float x = alignment switch
            {
                LineAlignment.Right  => right - lineWidth,
                LineAlignment.Center => left + (right - left - lineWidth) / 2,
                _                    => left
            };
            float baseline = CardCanvas.Baseline(lineTop, _style.Size, _style.Leading);
            foreach (Token token in line)
            {
                DrawToken(token, x, lineTop, baseline);
                x += token.Width;
            }
        }

        return Height;
    }

    private void DrawToken(Token token, float x, float lineTop, float baseline)
    {
        float size = _style.Size;
        SpanStyle style = token.Style;
        SKColor? box = style.HasFlag(SpanStyle.Placeholder) ? CardPalette.Signal : style.HasFlag(SpanStyle.Code) ? CardPalette.Sunken : null;
        if (box is { } boxColor)
        {
            float boxHeight = size * BOX_HEIGHT_RATIO;
            float boxTop = lineTop + (LineHeight - boxHeight) / 2;
            _canvas.Fill(new SKRect(x, boxTop, x + token.Width, boxTop + boxHeight), boxColor);
        }

        if (style.HasFlag(SpanStyle.Emphasis))
            _canvas.Fill(new SKRect(x, baseline - size * HIGHLIGHT_TOP, x + token.Width, baseline + size * HIGHLIGHT_BOTTOM), CardPalette.Signal);

        SKColor color = Color(style);
        if (!token.IsSpace) _canvas.Text(token.Text, token.Typeface, token.Size, color, x + token.PadLeft, baseline);
        if (style.HasFlag(SpanStyle.Strike))
            _canvas.Fill(new SKRect(x, baseline - size * STRIKE_OFFSET, x + token.Width, baseline - size * STRIKE_OFFSET + STRIKE_WIDTH), color);
        if (style.HasFlag(SpanStyle.Link))
            _canvas.Fill(new SKRect(x, baseline + UNDERLINE_OFFSET, x + token.Width, baseline + UNDERLINE_OFFSET + UNDERLINE_WIDTH), CardPalette.SignalInk);
    }

    private SKColor Color(SpanStyle style) =>
        style switch
        {
            _ when style.HasFlag(SpanStyle.Placeholder) => CardPalette.Ink,
            _ when style.HasFlag(SpanStyle.Secondary)   => CardPalette.InkTertiary,
            _                                           => _style.Color
        };

    private static List<Token> Tokenize(CardCanvas canvas, IReadOnlyList<RichSpan> spans, RichTextStyle style, float maxWidth)
    {
        List<Token> tokens = [];
        foreach (RichSpan span in spans)
        {
            (SKTypeface typeface, float size) = Face(canvas, span.Style, style);
            int first = tokens.Count;
            List<string> word = [];
            TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(span.Text);
            while (enumerator.MoveNext())
            {
                string element = enumerator.GetTextElement();
                if (element == LINE_BREAK || string.IsNullOrWhiteSpace(element))
                {
                    Flush();
                    tokens.Add(new Token
                    {
                        Text     = element == LINE_BREAK ? string.Empty : " ",
                        Style    = span.Style,
                        Typeface = typeface,
                        Size     = size,
                        Width    = element == LINE_BREAK ? 0 : canvas.Painter.Measure(" ", typeface, size),
                        IsSpace  = element != LINE_BREAK,
                        IsBreak  = element == LINE_BREAK
                    });
                    continue;
                }

                if (IsClosingPunctuation(element))
                {
                    // 句末标点不放在行首：并入前一词。
                    if (word.Count > 0) word.Add(element);
                    else if (tokens.Count > first && !tokens[^1].IsSpace && !tokens[^1].IsBreak)
                        tokens[^1] = Measure(canvas, tokens[^1].Text + element, span.Style, typeface, size);
                    else word.Add(element);
                    if (IsStandalone(element)) Flush();
                    continue;
                }

                if (IsStandalone(element))
                {
                    Flush();
                    word.Add(element);
                    Flush();
                }
                else word.Add(element);
            }

            Flush();
            // 行内代码与占位的底色比文字左右各宽一点。
            if (span.Style.HasFlag(SpanStyle.Code) || span.Style.HasFlag(SpanStyle.Placeholder))
            {
                int firstText = tokens.FindIndex(first, token => !token.IsSpace && !token.IsBreak);
                int lastText = tokens.FindLastIndex(token => !token.IsSpace && !token.IsBreak);
                if (firstText >= first && lastText >= firstText)
                {
                    tokens[firstText] = tokens[firstText].Padded(BOX_PADDING, 0);
                    tokens[lastText] = tokens[lastText].Padded(0, BOX_PADDING);
                }
            }

            continue;

            void Flush()
            {
                if (word.Count == 0) return;
                Token token = Measure(canvas, string.Concat(word), span.Style, typeface, size);
                if (token.Width <= maxWidth) tokens.Add(token);
                else tokens.AddRange(word.Select(element => Measure(canvas, element, span.Style, typeface, size)));
                word.Clear();
            }
        }

        return tokens;
    }

    private static Token Measure(CardCanvas canvas, string text, SpanStyle style, SKTypeface typeface, float size) =>
        new()
        {
            Text     = text,
            Style    = style,
            Typeface = typeface,
            Size     = size,
            Width    = canvas.Painter.Measure(text, typeface, size)
        };

    private static (SKTypeface Typeface, float Size) Face(CardCanvas canvas, SpanStyle span, RichTextStyle style) =>
        span switch
        {
            _ when span.HasFlag(SpanStyle.Code)   => (canvas.Fonts.Mono, style.Size * CODE_SIZE_RATIO),
            _ when span.HasFlag(SpanStyle.Strong) => (style.StrongTypeface, style.Size),
            _                                     => (style.Typeface, style.Size)
        };

    // 字母、数字与 ASCII 标点连成一词；其余字素（汉字、全角标点、emoji 等）各自成词，前后都可断开。
    private static bool IsStandalone(string element) =>
        !element.All(character => char.IsAsciiLetterOrDigit(character) || (char.IsAscii(character) && char.IsPunctuation(character)));

    private static bool IsClosingPunctuation(string element) =>
        element.Length == 1
        && char.IsPunctuation(element[0])
        && CharUnicodeInfo.GetUnicodeCategory(element[0]) is not (UnicodeCategory.OpenPunctuation or UnicodeCategory.InitialQuotePunctuation);

    private static List<Token> Trim(List<Token> line)
    {
        while (line.Count > 0 && line[^1].IsSpace) line.RemoveAt(line.Count - 1);
        return line;
    }

    private sealed class Token
    {
        public required string     Text     { get; init; }
        public required SpanStyle  Style    { get; init; }
        public required SKTypeface Typeface { get; init; }
        public required float      Size     { get; init; }
        public required float      Width    { get; init; }
        public          float      PadLeft  { get; init; }
        public          bool       IsSpace  { get; init; }
        public          bool       IsBreak  { get; init; }

        public Token Padded(float left, float right) =>
            new()
            {
                Text     = Text,
                Style    = Style,
                Typeface = Typeface,
                Size     = Size,
                Width    = Width + left + right,
                PadLeft  = PadLeft + left,
                IsSpace  = IsSpace,
                IsBreak  = IsBreak
            };
    }
}
