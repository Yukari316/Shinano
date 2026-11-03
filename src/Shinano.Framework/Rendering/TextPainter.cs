using System.Globalization;
using System.Text;
using SkiaSharp;
using SkiaSharp.HarfBuzz;

namespace Shinano.Framework.Rendering;

/// <summary>
/// 文字的测量、换行与绘制。按字素（用户可见字符）回退字体：主字体缺字形的字素依次改用回退字体中第一个有该字形的
/// （如英文字体中的汉字改用中文字体，再缺的改用 emoji 字体）；每段同字体的文字经 HarfBuzz 整形后绘制，
/// ZWJ 组合的 emoji 可以合成为一个图形。线程安全：不持有可变状态，每次调用使用独立的字体对象与整形器。
/// </summary>
public sealed class TextPainter
{
    private const string ELLIPSIS = "…";

    private readonly IReadOnlyList<SKTypeface> _fallbacks;

    /// <summary>创建绘制辅助。</summary>
    /// <param name="fallbacks">主字体缺字形时按顺序尝试的回退字体；都没有该字形时仍用最后一个。</param>
    public TextPainter(IReadOnlyList<SKTypeface> fallbacks)
    {
        _fallbacks = fallbacks;
    }

    /// <summary>测量一行文字的宽度，单位与 <paramref name="size" /> 相同。</summary>
    public float Measure(string text, SKTypeface typeface, float size) =>
        Runs(text, typeface, size).Sum(run => Shape(run.Text, run.Typeface, size));

    /// <summary>在基线 <paramref name="baseline" /> 处从 <paramref name="x" /> 起绘制一行文字，不换行。</summary>
    public void Draw(SKCanvas canvas, string text, SKTypeface typeface, float size, float x, float baseline, SKPaint paint)
    {
        foreach ((string run, SKTypeface runTypeface) in Runs(text, typeface, size))
        {
            using SKFont font = new(runTypeface, size) { Subpixel = true };
            canvas.DrawShapedText(run, x, baseline, SKTextAlign.Left, font, paint);
            x += Shape(run, runTypeface, size);
        }
    }

    /// <summary>
    /// 按宽度换行：优先在空白处或汉字、emoji 等之后断开，连续的字母数字过长时在字素处断开；保留文中的换行符。
    /// 超过 <paramref name="maxLines" /> 时截断，最后一行以省略号结尾。
    /// </summary>
    public IReadOnlyList<string> Wrap(string text, SKTypeface typeface, float size, float maxWidth, int maxLines = int.MaxValue)
    {
        List<string> lines = [];
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            WrapParagraph(paragraph, typeface, size, maxWidth, lines);
            if (lines.Count > maxLines) break;
        }

        if (lines.Count <= maxLines) return lines;

        List<string> kept = lines.GetRange(0, maxLines);
        string last = kept[^1];
        while (last.Length > 0 && Measure(last + ELLIPSIS, typeface, size) > maxWidth)
            last = last[..^StringInfo.GetNextTextElementLength(last, LastElementStart(last))];
        kept[^1] = last + ELLIPSIS;
        return kept;
    }

    private void WrapParagraph(string paragraph, SKTypeface typeface, float size, float maxWidth, List<string> lines)
    {
        List<string> elements = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(paragraph);
        while (enumerator.MoveNext()) elements.Add(enumerator.GetTextElement());

        int start = 0;
        while (start < elements.Count)
        {
            float width = 0;
            int end = start;
            int lastBreak = -1;
            while (end < elements.Count)
            {
                float next = width + Measure(elements[end], typeface, size);
                if (next > maxWidth && end > start) break;
                width = next;
                if (IsBreakAfter(elements[end])) lastBreak = end + 1;
                end++;
            }

            // 在单词中间溢出时退回到最近的断点；整行没有断点时只能在字素处断开。
            if (end < elements.Count && lastBreak > start && lastBreak < end) end = lastBreak;
            lines.Add(string.Concat(elements.Skip(start).Take(end - start)).TrimEnd());
            start = end;
            while (start < elements.Count && string.IsNullOrWhiteSpace(elements[start])) start++;
        }

        if (elements.Count == 0) lines.Add(string.Empty);
    }

    // 空白之后可以断开；汉字、emoji 等非拉丁字母数字的字素前后都可以断开。
    private static bool IsBreakAfter(string element) =>
        string.IsNullOrWhiteSpace(element) || !element.All(character => char.IsAsciiLetterOrDigit(character) || char.IsPunctuation(character));

    private static int LastElementStart(string text)
    {
        int index = 0;
        int last = 0;
        while (index < text.Length)
        {
            last = index;
            index += StringInfo.GetNextTextElementLength(text, index);
        }

        return last;
    }

    private IEnumerable<(string Text, SKTypeface Typeface)> Runs(string text, SKTypeface typeface, float size)
    {
        List<(SKTypeface Typeface, SKFont Font)> candidates = [.. new[] { typeface }.Concat(_fallbacks).Select(face => (face, new SKFont(face, size)))];
        try
        {
            return Split(text, typeface, candidates);
        }
        finally
        {
            foreach ((_, SKFont font) in candidates) font.Dispose();
        }
    }

    private List<(string Text, SKTypeface Typeface)> Split(string text, SKTypeface typeface, IReadOnlyList<(SKTypeface Typeface, SKFont Font)> candidates)
    {
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        StringBuilder current = new();
        SKTypeface? currentTypeface = null;
        List<(string, SKTypeface)> runs = [];
        while (enumerator.MoveNext())
        {
            string element = enumerator.GetTextElement();
            // 空白沿用当前字体，避免英文词间的空格被切成单独一段改用其他字体。
            SKTypeface chosen = string.IsNullOrWhiteSpace(element) && currentTypeface is not null
                ? currentTypeface
                : candidates.FirstOrDefault(candidate => candidate.Font.ContainsGlyphs(element)).Typeface ?? _fallbacks.LastOrDefault() ?? typeface;
            if (currentTypeface is not null && chosen != currentTypeface)
            {
                runs.Add((current.ToString(), currentTypeface));
                current.Clear();
            }

            current.Append(element);
            currentTypeface = chosen;
        }

        if (currentTypeface is not null) runs.Add((current.ToString(), currentTypeface));
        return runs;
    }

    private static float Shape(string text, SKTypeface typeface, float size)
    {
        using SKFont font = new(typeface, size);
        using SKShaper shaper = new(typeface);
        return shaper.Shape(text, font).Width;
    }
}
