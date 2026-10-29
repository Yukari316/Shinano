using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.Entertainment;

/// <summary>
/// 解析"选择A还是B还是C"。消息只能由文本、图片与 QQ 表情组成，按文本中的"还是"把整串消息段切成选项，
/// 图文混合的选项作为一个整体；每个选项去掉首尾空白，没有内容的选项丢弃。
/// </summary>
public static class ChoiceParser
{
    /// <summary>指令前缀。</summary>
    public const string PREFIX = "选择";

    /// <summary>选项分隔词。</summary>
    public const string SEPARATOR = "还是";

    private const int MIN_OPTIONS = 2;

    /// <summary>解析消息。</summary>
    /// <returns>至少两个选项，每个选项是一组可直接发送的消息段；消息不是有效的选择时为 null。</returns>
    public static IReadOnlyList<MessageBody>? Parse(MessageBody body)
    {
        if (body.Count == 0 || body[0] is not TextSegment first) return null;
        if (body.Any(segment => segment is not (TextSegment or ImageSegment or FaceSegment))) return null;

        string head = first.Text.TrimStart();
        if (!head.StartsWith(PREFIX, StringComparison.Ordinal)) return null;

        List<List<Segment>> options = [[]];
        foreach (Segment segment in (Segment[]) [new TextSegment { Text = head[PREFIX.Length..] }, .. body.Skip(1)])
        {
            if (segment is not TextSegment text)
            {
                options[^1].Add(segment);
                continue;
            }

            string[] parts = text.Text.Split(SEPARATOR);
            options[^1].Add(new TextSegment { Text = parts[0] });
            foreach (string part in parts.Skip(1))
                options.Add([new TextSegment { Text = part }]);
        }

        List<MessageBody> result = [.. options.Select(Trim).Where(option => option.Count > 0)];
        return result.Count >= MIN_OPTIONS ? result : null;
    }

    // 去掉选项首尾的空白；首尾只剩空白的文本段整段去掉。
    private static MessageBody Trim(List<Segment> option)
    {
        List<Segment> segments = [.. option];
        while (segments.Count > 0 && segments[0] is TextSegment head)
        {
            string text = head.Text.TrimStart();
            if (text.Length > 0)
            {
                segments[0] = head with { Text = text };
                break;
            }

            segments.RemoveAt(0);
        }

        while (segments.Count > 0 && segments[^1] is TextSegment tail)
        {
            string text = tail.Text.TrimEnd();
            if (text.Length > 0)
            {
                segments[^1] = tail with { Text = text };
                break;
            }

            segments.RemoveAt(segments.Count - 1);
        }

        segments.RemoveAll(segment => segment is TextSegment { Text.Length: 0 });
        return new MessageBody(segments);
    }
}
