using Shinano.Framework.Rendering;

namespace Shinano.Plugin.Qa;

/// <summary>"看看有人问"的一页：新录入的在前，序号跨页连续，正则词条带 RG 标签。</summary>
public sealed class QaListPage
{
    private const string TITLE     = "有人问";
    private const string FOOTER    = "发送 看看有人问 页码 翻页";
    private const string REGEX_TAG = "RG";

    private readonly int _offset;

    private QaListPage(IReadOnlyList<QaEntry> entries, int number, int count, int total, int offset)
    {
        Entries = entries;
        Number  = number;
        Count   = count;
        Total   = total;
        _offset = offset;
    }

    /// <summary>本页的词条。</summary>
    public IReadOnlyList<QaEntry> Entries { get; }

    /// <summary>页码，从 1 开始。</summary>
    public int Number { get; }

    /// <summary>总页数。</summary>
    public int Count { get; }

    /// <summary>词条总数。</summary>
    public int Total { get; }

    /// <summary>取出第 <paramref name="number" /> 页。</summary>
    /// <param name="entries">群的全部词条，越新越靠前；不能为空。</param>
    /// <param name="number">页码。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <returns>该页；页码超出范围时为 null。</returns>
    public static QaListPage? Take(IReadOnlyList<QaEntry> entries, int number, int pageSize)
    {
        int count = (entries.Count + pageSize - 1) / pageSize;
        if (number < 1 || number > count) return null;
        int offset = (number - 1) * pageSize;
        return new QaListPage([.. entries.Skip(offset).Take(pageSize)], number, count, entries.Count, offset);
    }

    /// <summary>列表卡片。</summary>
    public ListCard ToCard() =>
        new()
        {
            Title    = TITLE,
            Subtitle = $"本群共 {Total} 条",
            Page     = Number,
            Pages    = Count,
            Items    = [.. Entries.Select((entry, index) => new ListCardItem
            {
                Index = _offset + index + 1,
                Tag   = entry.MatchType == QaMatchType.Regex ? REGEX_TAG : null,
                Text  = entry.Question
            })],
            Hint = FOOTER
        };

    /// <summary>卡片无法绘制时代替发送的文字。</summary>
    public string ToText() =>
        string.Join(
            '\n',
            (string[])
            [
                $"{TITLE}（第 {Number}/{Count} 页 · 共 {Total} 条）",
                .. Entries.Select((entry, index) =>
                    $"{_offset + index + 1}. {(entry.MatchType == QaMatchType.Regex ? $"[{REGEX_TAG}] " : string.Empty)}{entry.Question}"),
                FOOTER
            ]);
}
