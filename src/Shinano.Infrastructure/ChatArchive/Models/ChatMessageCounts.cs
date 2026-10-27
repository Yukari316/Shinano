using Sora.Entities.Segments;

namespace Shinano.Infrastructure.ChatArchive.Models;

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
