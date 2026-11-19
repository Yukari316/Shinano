using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Shinano.Plugin.Qa;

/// <summary>
/// 在一个群的词条中找出命中的一条：全字与正则统一按创建先后排序，越新越优先，只返回第一条命中。
/// 正则不加隐式选项，每次匹配有超时，超时视为未命中并告警。线程安全。
/// </summary>
public sealed class QaMatcher
{
    private readonly ConcurrentDictionary<(long Id, int Version, TimeSpan Timeout), Regex> _regexes = new();
    private readonly ILogger<QaMatcher>                                                    _logger;

    /// <summary>创建匹配器。</summary>
    public QaMatcher(ILogger<QaMatcher> logger)
    {
        _logger = logger;
    }

    /// <summary>检查正则表达式能否编译。</summary>
    /// <returns>错误原因；可以编译时为 null。</returns>
    public static string? Validate(string pattern)
    {
        try
        {
            _ = new Regex(pattern);
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
    }

    /// <summary>找出命中的词条。</summary>
    /// <param name="entries">群的词条，越新越靠前。</param>
    /// <param name="text">去掉首尾空白的消息文本。</param>
    /// <param name="timeout">每个正则的匹配超时。</param>
    public QaEntry? Match(IReadOnlyList<QaEntry> entries, string text, TimeSpan timeout)
    {
        foreach (QaEntry entry in entries)
        {
            if (entry.MatchType == QaMatchType.Full)
            {
                if (entry.Question == text) return entry;
                continue;
            }

            try
            {
                Regex regex = _regexes.GetOrAdd((entry.Id, entry.Version, timeout), key => new Regex(entry.Question, RegexOptions.None, key.Timeout));
                if (regex.IsMatch(text)) return entry;
            }
            catch (RegexMatchTimeoutException)
            {
                _logger.LogWarning("正则词条 {EntryId} 在群 {GroupId} 匹配超时（{Timeout} 毫秒），视为未命中", entry.Id, entry.GroupId, timeout.TotalMilliseconds);
            }
            catch (ArgumentException exception)
            {
                // 录入时已校验，只有数据被外部改动时才会出现。
                _logger.LogWarning(exception, "正则词条 {EntryId} 无法编译，跳过", entry.Id);
            }
        }

        return null;
    }
}

/// <summary>词条的匹配方式，数值存入数据库。</summary>
public enum QaMatchType
{
    /// <summary>去掉首尾空白后与问题完全相同。</summary>
    Full = 0,

    /// <summary>正则表达式匹配。</summary>
    Regex = 1
}
