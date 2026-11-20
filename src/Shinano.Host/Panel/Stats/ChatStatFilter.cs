using Shinano.Infrastructure.ChatArchive;

namespace Shinano.Host.Panel.Stats;

/// <summary>统计查询条件。日期为配置时区的自然日，两端都包含。</summary>
public sealed class ChatStatFilter
{
    /// <summary>机器人账号。</summary>
    public required long SelfId { get; init; }

    /// <summary>起始日期。</summary>
    public required DateOnly From { get; init; }

    /// <summary>结束日期。</summary>
    public required DateOnly To { get; init; }

    /// <summary>会话范围。</summary>
    public required ChatStatScope Scope { get; init; }

    /// <summary>只统计某个会话；为 null 时不限。</summary>
    public long? ConversationId { get; init; }

    /// <summary>
    /// 与 <see cref="ConversationId" /> 一起指定会话的类型；为 null 时不限。好友与临时会话的 ID 都是对方的 QQ 号，只按 ID 会混在一起。
    /// </summary>
    public ChatConversationType? ConversationType { get; init; }

    /// <summary>是否包含 Bot 自己发送的消息。</summary>
    public required bool IncludeBot { get; init; }
}

/// <summary>统计查询的会话范围。</summary>
public enum ChatStatScope
{
    /// <summary>全部会话。</summary>
    All,

    /// <summary>群聊。</summary>
    Group,

    /// <summary>私聊：好友私聊与群临时会话。</summary>
    Private
}

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
