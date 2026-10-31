using System.Text;
using Shinano.Plugin.Qa.Models;
using Sora.Entities.Message;
using Sora.Entities.Segments;

namespace Shinano.Plugin.Qa;

/// <summary>
/// 录入消息的解析。文字录入"有人问…你答…"按第一个"你答"拆分，问题只能是文字，回答可含文字、图片、QQ 表情与 @；
/// 引用录入"记录有人问…"的问题来自文字，回答是被引用的整条消息，另可含语音、视频与文件。
/// </summary>
public static class QaRecordingParser
{
    /// <summary>全字匹配的文字录入前缀。</summary>
    public const string INLINE_PREFIX = "有人问";

    /// <summary>正则的文字录入前缀。</summary>
    public const string INLINE_REGEX_PREFIX = "RG有人问";

    /// <summary>全字匹配的引用录入前缀。</summary>
    public const string QUOTE_PREFIX = "记录有人问";

    /// <summary>正则的引用录入前缀。</summary>
    public const string QUOTE_REGEX_PREFIX = "记录RG有人问";

    /// <summary>问题与回答的分隔词。</summary>
    public const string ANSWER_MARK = "你答";

    /// <summary>问题与正则表达式的最大长度（字符）。</summary>
    public const int MAX_QUESTION_LENGTH = 200;

    /// <summary>解析"有人问…你答…"或"RG有人问…你答…"。</summary>
    /// <param name="body">录入消息。</param>
    /// <param name="prefix"><see cref="INLINE_PREFIX" /> 或 <see cref="INLINE_REGEX_PREFIX" />。</param>
    public static QaParseResult ParseInline(MessageBody body, string prefix)
    {
        if (body.Count == 0 || body[0] is not TextSegment first) return QaParseResult.Fail(QaParseError.Malformed);
        string head = first.Text.TrimStart();
        if (!head.StartsWith(prefix, StringComparison.Ordinal)) return QaParseResult.Fail(QaParseError.Malformed);

        StringBuilder question = new();
        bool questionHasOther = false;
        bool separated = false;
        List<Segment> answer = [];
        foreach (Segment segment in (Segment[]) [new TextSegment { Text = head[prefix.Length..] }, .. body.Skip(1)])
        {
            if (separated)
            {
                answer.Add(segment);
                continue;
            }

            if (segment is not TextSegment text)
            {
                questionHasOther = true;
                continue;
            }

            int mark = text.Text.IndexOf(ANSWER_MARK, StringComparison.Ordinal);
            if (mark < 0)
            {
                question.Append(text.Text);
                continue;
            }

            question.Append(text.Text[..mark]);
            answer.Add(new TextSegment { Text = text.Text[(mark + ANSWER_MARK.Length)..] });
            separated = true;
        }

        if (!separated) return QaParseResult.Fail(QaParseError.Malformed);
        if (questionHasOther) return QaParseResult.Fail(QaParseError.QuestionNotText);
        return Build(question.ToString(), answer, allowMedia: false);
    }

    /// <summary>
    /// 解析引用录入的指令消息：除引用与回复时自动带上的 @ 之外只能有文字，文字为前缀加问题。回答需另行取得被引用的消息。
    /// </summary>
    /// <param name="body">指令消息。</param>
    /// <param name="prefix"><see cref="QUOTE_PREFIX" /> 或 <see cref="QUOTE_REGEX_PREFIX" />。</param>
    public static QaParseResult ParseQuoteCommand(MessageBody body, string prefix)
    {
        ReplySegment? reply = body.OfType<ReplySegment>().FirstOrDefault();
        if (reply is null) return QaParseResult.Fail(QaParseError.Malformed);
        if (body.Any(segment => segment is not (TextSegment or ReplySegment or MentionSegment))) return QaParseResult.Fail(QaParseError.Unsupported);

        string text = body.GetText().Trim();
        if (!text.StartsWith(prefix, StringComparison.Ordinal)) return QaParseResult.Fail(QaParseError.Malformed);
        string question = text[prefix.Length..].Trim();
        return question.Length == 0
            ? QaParseResult.Fail(QaParseError.Malformed)
            : new QaParseResult
            {
                Question = question,
                Reply    = reply
            };
    }

    /// <summary>把被引用的整条消息解析为回答；其中的引用段去掉，不支持的内容使整条回答无效。</summary>
    public static QaParseResult ParseQuotedAnswer(string question, MessageBody quoted) =>
        Build(question, [.. quoted.Where(segment => segment is not ReplySegment)], allowMedia: true);

    private static QaParseResult Build(string question, List<Segment> answer, bool allowMedia)
    {
        string trimmed = question.Trim();
        if (trimmed.Length == 0) return QaParseResult.Fail(QaParseError.Malformed);

        List<QaDraftPart> parts = [];
        foreach (Segment segment in answer)
        {
            if (ToDraft(segment, allowMedia) is not { } part) return QaParseResult.Fail(QaParseError.Unsupported);
            parts.Add(part);
        }

        parts = Trim(parts);
        return parts.Count == 0
            ? QaParseResult.Fail(QaParseError.Malformed)
            : new QaParseResult
            {
                Question = trimmed,
                Answer   = parts
            };
    }

    private static QaDraftPart? ToDraft(Segment segment, bool allowMedia) =>
        segment switch
        {
            TextSegment text => new QaDraftPart
            {
                Kind   = QaPartKind.Text,
                Source = text,
                Text   = text.Text
            },
            FaceSegment face => new QaDraftPart
            {
                Kind      = QaPartKind.Face,
                Source    = face,
                FaceId    = face.FaceId,
                LargeFace = face.IsLarge
            },
            MentionSegment mention => new QaDraftPart
            {
                Kind   = QaPartKind.Mention,
                Source = mention,
                UserId = mention.Target
            },
            ImageSegment image => new QaDraftPart
            {
                Kind       = QaPartKind.Image,
                Source     = image,
                Url        = image.Url,
                ResourceId = image.ResourceId
            },
            AudioSegment audio when allowMedia => new QaDraftPart
            {
                Kind       = QaPartKind.Audio,
                Source     = audio,
                Url        = audio.Url,
                ResourceId = audio.ResourceId
            },
            VideoSegment video when allowMedia => new QaDraftPart
            {
                Kind       = QaPartKind.Video,
                Source     = video,
                Url        = video.Url,
                ResourceId = video.ResourceId
            },
            FileSegment file when allowMedia => new QaDraftPart
            {
                Kind         = QaPartKind.File,
                Source       = file,
                FileId       = file.FileId,
                FileName     = file.FileName,
                DeclaredSize = file.FileSize > 0 ? file.FileSize : null
            },
            _ => null
        };

    // 去掉回答首尾的空白；首尾只剩空白的文字段整段去掉，空文字段一并去掉。
    private static List<QaDraftPart> Trim(List<QaDraftPart> parts)
    {
        List<QaDraftPart> result = [.. parts.Where(part => part.Kind != QaPartKind.Text || part.Text is { Length: > 0 })];
        while (result.Count > 0 && result[0] is { Kind: QaPartKind.Text } head)
        {
            string text = head.Text?.TrimStart() ?? string.Empty;
            if (text.Length > 0)
            {
                result[0] = WithText(head, text);
                break;
            }

            result.RemoveAt(0);
        }

        while (result.Count > 0 && result[^1] is { Kind: QaPartKind.Text } tail)
        {
            string text = tail.Text?.TrimEnd() ?? string.Empty;
            if (text.Length > 0)
            {
                result[^1] = WithText(tail, text);
                break;
            }

            result.RemoveAt(result.Count - 1);
        }

        return result;
    }

    private static QaDraftPart WithText(QaDraftPart part, string text) =>
        new()
        {
            Kind   = QaPartKind.Text,
            Source = new TextSegment { Text = text },
            Text   = text
        };
}
