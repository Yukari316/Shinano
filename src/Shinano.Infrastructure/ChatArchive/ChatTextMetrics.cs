using System.Buffers;
using System.Globalization;
using System.Text;
using Sora.Entities.Segments;

namespace Shinano.Infrastructure.ChatArchive;

/// <summary>聊天字数规则。</summary>
public static class ChatTextMetrics
{
    private const int VARIATION_SELECTORS_START            = 0xFE00;
    private const int VARIATION_SELECTORS_END              = 0xFE0F;
    private const int VARIATION_SELECTORS_SUPPLEMENT_START = 0xE0100;
    private const int VARIATION_SELECTORS_SUPPLEMENT_END   = 0xE01EF;

    /// <summary>
    /// 按用户可见字符计数：以扩展字素簇为单位，完整 emoji（含组合序列、肤色与国旗）计为 1；
    /// 空白、控制字符、格式字符（如零宽空格 U+200B、U+2060）与单独出现的变体选择符（如 U+FE0F）不计入。
    /// 孤立的代理项按 1 个字符计。
    /// </summary>
    public static int CountVisibleCharacters(string text)
    {
        int count = 0;
        int index = 0;
        while (index < text.Length)
        {
            int length = StringInfo.GetNextTextElementLength(text, index);
            if (Rune.DecodeFromUtf16(text.AsSpan(index), out Rune first, out _) != OperationStatus.Done || IsVisible(first)) count++;
            index += length;
        }

        return count;
    }

    private static bool IsVisible(Rune rune) =>
        !Rune.IsWhiteSpace(rune)
        && !Rune.IsControl(rune)
        && Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format
        && !IsVariationSelector(rune.Value);

    private static bool IsVariationSelector(int value) =>
        value is >= VARIATION_SELECTORS_START and <= VARIATION_SELECTORS_END
              or >= VARIATION_SELECTORS_SUPPLEMENT_START and <= VARIATION_SELECTORS_SUPPLEMENT_END;
}

/// <summary>一条消息计入发言统计的数量。</summary>
public sealed class ChatMessageCounts
{
    /// <summary>可见字数，只统计文本段。</summary>
    public int CharCount { get; init; }

    /// <summary>图片数，含表情包与商城表情。</summary>
    public int ImageCount { get; init; }

    /// <summary>语音数。</summary>
    public int AudioCount { get; init; }

    /// <summary>视频数。</summary>
    public int VideoCount { get; init; }

    /// <summary>文件数。</summary>
    public int FileCount { get; init; }

    /// <summary>
    /// 统计消息顶层段：媒体按段出现次数计数；引用与合并转发中的历史内容不计入；
    /// @、表情等非文本段不计字数。
    /// </summary>
    public static ChatMessageCounts FromSegments(IEnumerable<Segment> segments)
    {
        int chars  = 0;
        int images = 0;
        int audios = 0;
        int videos = 0;
        int files  = 0;
        foreach (Segment segment in segments)
        {
            switch (segment)
            {
                case TextSegment text:
                    chars += ChatTextMetrics.CountVisibleCharacters(text.Text);
                    break;
                case ImageSegment:
                case MarketFaceSegment:
                    images++;
                    break;
                case AudioSegment:
                    audios++;
                    break;
                case VideoSegment:
                    videos++;
                    break;
                case FileSegment:
                    files++;
                    break;
            }
        }

        return new ChatMessageCounts
        {
            CharCount  = chars,
            ImageCount = images,
            AudioCount = audios,
            VideoCount = videos,
            FileCount  = files
        };
    }
}
