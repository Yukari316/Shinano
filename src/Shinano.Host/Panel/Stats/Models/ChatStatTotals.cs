namespace Shinano.Host.Panel.Stats.Models;

/// <summary>按某个维度汇总的发言统计；未参与分组的维度为默认值。</summary>
public sealed class ChatStatTotals
{
    /// <summary>统计日期（按日汇总时）。</summary>
    public DateTime Date { get; set; }

    /// <summary>会话类型（按会话汇总时）。</summary>
    public short ConversationType { get; set; }

    /// <summary>会话 ID（按会话汇总时）。</summary>
    public long ConversationId { get; set; }

    /// <summary>发言者（按发言者汇总时）。</summary>
    public long SenderId { get; set; }

    /// <summary>发言者人数（按会话汇总时）。</summary>
    public int Senders { get; set; }

    /// <summary>消息条数。</summary>
    public int Messages { get; set; }

    /// <summary>字数。</summary>
    public int Chars { get; set; }

    /// <summary>图片数。</summary>
    public int Images { get; set; }

    /// <summary>语音数。</summary>
    public int Audios { get; set; }

    /// <summary>视频数。</summary>
    public int Videos { get; set; }

    /// <summary>文件数。</summary>
    public int Files { get; set; }
}
