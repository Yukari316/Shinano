using System.Drawing;
using System.Text;
using CSharpMath.Atom;
using CSharpMath.Rendering.FrontEnd;
using SkiaSharp;
using Typography.OpenFont;
using MathFonts = CSharpMath.Rendering.BackEnd.Fonts;
using MathPath = CSharpMath.Rendering.FrontEnd.Path;

namespace Shinano.Host.Rendering;

/// <summary>
/// 基于 <c>CSharpMath.Rendering</c> 与自写 SkiaSharp 4 画布适配层的 LaTeX 数学公式渲染器。
/// CSharpMath 自带字体没有中文字形，公式含中文时加入宿主的中文后备字体（<see cref="HostFonts.MATH_CJK_FILE" />）。
/// 单条公式解析或排版失败、或含有任何字体都没有的字符时返回 null，由调用方回退为原样纯文本。
/// </summary>
internal static class LatexMathRenderer
{
    private const float PADDING         = 1.5f;
    private const int   FIRST_NON_ASCII = 0x80;

    // CSharpMath 内部共享字体缓存，串行排版避免潜在并发初始化竞争。
    private static readonly Lock Sync = new();

    // 约 2 MB，首次遇到含中文的公式时才加载；加载失败时缓存 null，含中文的公式一律回退原文。
    private static readonly Lazy<Typeface?> CjkTypeface = new(LoadCjkTypeface);

    /// <summary>
    /// 将 Markdown 中的 <c>\[...\]</c> 与 <c>\(...\)</c> 转为 Markdig 数学扩展识别的 <c>$$...$$</c> 与 <c>$...$</c>，
    /// 完整跳过围栏代码块与行内代码段。
    /// </summary>
    public static string NormalizeDelimiters(string markdown)
    {
        if (!markdown.Contains(@"\(", StringComparison.Ordinal) && !markdown.Contains(@"\[", StringComparison.Ordinal))
            return markdown;

        string normalized = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        StringBuilder output = new(normalized.Length + 32);
        string[] lines = normalized.Split('\n');
        char fenceChar = '\0';
        int fenceLength = 0;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (lineIndex > 0) output.Append('\n');
            string line = lines[lineIndex];
            string trimmed = line.TrimStart();

            if (fenceChar == '\0')
            {
                if (TryOpenFence(trimmed, out char ch, out int len))
                {
                    fenceChar = ch;
                    fenceLength = len;
                    output.Append(line);
                    continue;
                }
            }
            else
            {
                output.Append(line);
                if (IsCloseFence(trimmed, fenceChar, fenceLength))
                {
                    fenceChar = '\0';
                    fenceLength = 0;
                }

                continue;
            }

            output.Append(NormalizeLineOrBlock(lines, ref lineIndex));
        }

        return output.ToString();
    }

    /// <summary>
    /// 尝试排版一条 LaTeX 公式；语法非法、空公式或绘制异常时返回 null。
    /// </summary>
    public static RenderedMathFormula? TryRender(string latex, float fontSize, SKColor color, bool displayStyle)
    {
        string trimmed = latex.Trim();
        if (trimmed.Length == 0) return null;

        lock (Sync)
        {
            try
            {
                if (!TryResolveTypefaces(trimmed, out Typeface[] localTypefaces)) return null;

                SkiaMathPainter painter = new()
                {
                    LaTeX              = trimmed,
                    FontSize           = fontSize,
                    TextColor          = color,
                    LineStyle          = displayStyle ? LineStyle.Display : LineStyle.Text,
                    DisplayErrorInline = false,
                    LocalTypefaces     = localTypefaces
                };

                if (!string.IsNullOrEmpty(painter.ErrorMessage)) return null;

                RectangleF bounds = painter.Measure(float.PositiveInfinity);
                if (bounds.Width <= 0 || bounds.Height <= 0 || float.IsNaN(bounds.Width) || float.IsNaN(bounds.Height))
                    return null;

                float width = bounds.Width + PADDING * 2;
                float height = bounds.Height + PADDING * 2;
                // CSharpMath 坐标系原点位于基线，bounds.Y 为基线之上的负偏移（或顶部偏移），
                // 将画布平移使内容左上角落在 (PADDING, PADDING)。
                float drawX = PADDING - bounds.X;
                float drawY = PADDING - bounds.Y;
                float ascent = Math.Clamp(-bounds.Y + PADDING, height * 0.25f, height);

                using SKPictureRecorder recorder = new();
                SKCanvas canvas = recorder.BeginRecording(new SKRect(0, 0, width, height));
                painter.Draw(canvas, drawX, drawY);
                SKPicture picture = recorder.EndRecording();

                return new RenderedMathFormula
                {
                    Picture = picture,
                    Width   = width,
                    Height  = height,
                    Ascent  = ascent
                };
            }
            catch
            {
                return null;
            }
        }
    }

    // 缺字时 CSharpMath 不报错而画成方框：按码点核对字体覆盖，自带字体缺的字符由中文后备字体补齐，
    // 仍有字符没有任何字体覆盖（如 emoji）时放弃排版，由调用方按原文绘制。
    private static bool TryResolveTypefaces(string latex, out Typeface[] localTypefaces)
    {
        localTypefaces = [];
        int[] missing =
        [
            .. latex.EnumerateRunes()
                    .Select(rune => rune.Value)
                    .Where(code => code >= FIRST_NON_ASCII && !MathFonts.GlobalTypefaces.Any(typeface => typeface.GetGlyphIndex(code) != 0))
                    .Distinct()
        ];
        if (missing.Length == 0) return true;

        if (CjkTypeface.Value is not { } cjk || missing.Any(code => cjk.GetGlyphIndex(code) == 0)) return false;
        localTypefaces = [cjk];
        return true;
    }

    private static Typeface? LoadCjkTypeface()
    {
        string path = HostFonts.FontPath(AppContext.BaseDirectory, HostFonts.MATH_CJK_FILE);
        try
        {
            using FileStream stream = File.OpenRead(path);
            return new OpenFontReader().Read(stream);
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeLineOrBlock(string[] lines, ref int lineIndex)
    {
        string line = lines[lineIndex];
        // 处理跨行 \[ ... \] 块级公式。
        if (line.Trim() == @"\[")
        {
            StringBuilder block = new();
            block.Append("$$\n");
            int scan = lineIndex + 1;
            while (scan < lines.Length)
            {
                if (lines[scan].Trim() == @"\]")
                {
                    block.Append("$$");
                    lineIndex = scan;
                    return block.ToString();
                }

                block.Append(lines[scan]).Append('\n');
                scan++;
            }

            // 未闭合时保持原样。
            return line;
        }

        // 处理单行内的 \[...\] 与 \(...\)，跳过反引号行内代码。
        StringBuilder result = new(line.Length + 8);
        int index = 0;
        while (index < line.Length)
        {
            if (line[index] == '`')
            {
                int ticks = CountChar(line, index, '`');
                int close = FindClosingBackticks(line, index + ticks, ticks);
                if (close >= 0)
                {
                    result.Append(line, index, close + ticks - index);
                    index = close + ticks;
                    continue;
                }
            }

            if (index + 1 < line.Length && line[index] == '\\' && line[index + 1] is '(' or '[')
            {
                bool isDisplay = line[index + 1] == '[';
                string closeToken = isDisplay ? @"\]" : @"\)";
                int close = FindUnescaped(line, index + 2, closeToken);
                if (close >= 0)
                {
                    string inner = line[(index + 2)..close].Trim();
                    if (inner.Length > 0)
                    {
                        string delimiter = isDisplay ? "$$" : "$";
                        result.Append(delimiter).Append(inner).Append(delimiter);
                        index = close + 2;
                        continue;
                    }
                }
            }

            result.Append(line[index]);
            index++;
        }

        return result.ToString();
    }

    private static bool TryOpenFence(string trimmed, out char fenceChar, out int length)
    {
        fenceChar = '\0';
        length = 0;
        if (trimmed.Length < 3 || trimmed[0] is not ('`' or '~')) return false;
        int count = CountChar(trimmed, 0, trimmed[0]);
        if (count < 3) return false;
        fenceChar = trimmed[0];
        length = count;
        return true;
    }

    private static bool IsCloseFence(string trimmed, char fenceChar, int minLength)
    {
        if (trimmed.Length < minLength || trimmed[0] != fenceChar) return false;
        int count = CountChar(trimmed, 0, fenceChar);
        return count >= minLength && trimmed[count..].Trim().Length == 0;
    }

    private static int CountChar(string text, int start, char target)
    {
        int count = 0;
        while (start + count < text.Length && text[start + count] == target) count++;
        return count;
    }

    private static int FindClosingBackticks(string text, int start, int count)
    {
        int index = start;
        while (index < text.Length)
        {
            if (text[index] == '`')
            {
                int found = CountChar(text, index, '`');
                if (found == count) return index;
                index += found;
                continue;
            }

            index++;
        }

        return -1;
    }

    private static int FindUnescaped(string text, int start, string token)
    {
        int index = start;
        while (index <= text.Length - token.Length)
        {
            int pos = text.IndexOf(token, index, StringComparison.Ordinal);
            if (pos < 0) return -1;
            int backslashes = 0;
            for (int cursor = pos - 1; cursor >= start && text[cursor] == '\\'; cursor--) backslashes++;
            if (backslashes % 2 == 0) return pos;
            index = pos + token.Length;
        }

        return -1;
    }

    private sealed class SkiaMathPainter : MathPainter<SKCanvas, SKColor>
    {
        public override Color WrapColor(SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);

        public override SKColor UnwrapColor(Color color) => new(color.R, color.G, color.B, color.A);

        public override ICanvas WrapCanvas(SKCanvas canvas) => new SkiaMathCanvas(canvas);
    }

    private sealed class SkiaMathCanvas : ICanvas
    {
        private readonly SKCanvas _canvas;

        public SkiaMathCanvas(SKCanvas canvas) => _canvas = canvas;

        public float Width => _canvas.LocalClipBounds.Width;

        public float Height => _canvas.LocalClipBounds.Height;

        public Color DefaultColor { get; set; } = Color.Black;

        public Color? CurrentColor { get; set; }

        public PaintStyle CurrentStyle { get; set; } = PaintStyle.Fill;

        public MathPath StartNewPath() => new SkiaMathPath(this);

        public void DrawLine(float x1, float y1, float x2, float y2, float lineThickness)
        {
            using SKPaint paint = CreatePaint(SKPaintStyle.Stroke, lineThickness);
            _canvas.DrawLine(x1, y1, x2, y2, paint);
        }

        public void StrokeRect(float left, float top, float width, float height)
        {
            using SKPaint paint = CreatePaint(SKPaintStyle.Stroke, 1);
            _canvas.DrawRect(SKRect.Create(left, top, width, height), paint);
        }

        public void FillRect(float left, float top, float width, float height)
        {
            using SKPaint paint = CreatePaint(SKPaintStyle.Fill, 0);
            _canvas.DrawRect(SKRect.Create(left, top, width, height), paint);
        }

        public void Save() => _canvas.Save();

        public void Translate(float dx, float dy) => _canvas.Translate(dx, dy);

        public void Scale(float sx, float sy) => _canvas.Scale(sx, sy);

        public void Restore() => _canvas.Restore();

        internal void DrawPath(SKPath path, Color? foreground)
        {
            SKPaintStyle style = CurrentStyle == PaintStyle.Stroke ? SKPaintStyle.Stroke : SKPaintStyle.Fill;
            using SKPaint paint = CreatePaint(style, 1, foreground);
            _canvas.DrawPath(path, paint);
        }

        private SKPaint CreatePaint(SKPaintStyle style, float strokeWidth, Color? overrideColor = null)
        {
            Color color = overrideColor ?? CurrentColor ?? DefaultColor;
            return new SKPaint
            {
                Color       = new SKColor(color.R, color.G, color.B, color.A),
                Style       = style,
                StrokeWidth = strokeWidth,
                IsAntialias = true
            };
        }
    }

    private sealed class SkiaMathPath : MathPath
    {
        private readonly SkiaMathCanvas _owner;
        private readonly SKPathBuilder  _builder = new();

        public SkiaMathPath(SkiaMathCanvas owner) => _owner = owner;

        public override Color? Foreground { get; set; }

        public override void MoveTo(float x0, float y0) => _builder.MoveTo(x0, y0);

        public override void LineTo(float x1, float y1) => _builder.LineTo(x1, y1);

        public override void Curve3(float x1, float y1, float x2, float y2) => _builder.QuadTo(x1, y1, x2, y2);

        public override void Curve4(float x1, float y1, float x2, float y2, float x3, float y3) => _builder.CubicTo(x1, y1, x2, y2, x3, y3);

        public override void CloseContour() => _builder.Close();

        public override void Dispose()
        {
            using SKPath path = _builder.Detach();
            _owner.DrawPath(path, Foreground);
            _builder.Dispose();
        }
    }
}

/// <summary>已完成录制的单个 LaTeX 公式矢量图及其基线度量。</summary>
internal sealed class RenderedMathFormula : IDisposable
{
    /// <summary>公式矢量绘制指令。</summary>
    public required SKPicture Picture { get; init; }

    /// <summary>逻辑宽度。</summary>
    public required float Width { get; init; }

    /// <summary>逻辑高度。</summary>
    public required float Height { get; init; }

    /// <summary>顶边到基线的距离。</summary>
    public required float Ascent { get; init; }

    /// <inheritdoc />
    public void Dispose() => Picture.Dispose();
}
