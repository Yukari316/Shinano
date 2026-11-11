using System.Text.Json.Nodes;
using Shinano.Plugin.NB2Bridge.Models;
using Sora.Core.Enums;
using Sora.Entities.Events;
using Sora.Entities.Info;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>
/// 从 Sora 事件重建 Milky 事件 JSON 交给 NB2。Sora 不保留原始报文，字段取自 Sora 事件；Sora 没有而 Milky 要求的字段填中性值
/// （好友的 qid、备注为空字符串，分组为 0）。每个事件附带 <see cref="KEY_FIELD" />，控制插件按它回报处理结果；
/// 官方适配器的事件模型允许额外字段，NB2 插件不受影响。
/// </summary>
public static class MilkyEvents
{
    /// <summary>事件上的关联键字段名，与控制插件一致。</summary>
    public const string KEY_FIELD = "shinano_key";

    private const string GROUP  = "group";
    private const string FRIEND = "friend";

    /// <summary>事件类别；不交付给 NB2 的事件为 null（没有对应 Milky 事件的类型与临时会话的事件）。</summary>
    public static Nb2EventKind? Classify(BotEvent e) =>
        e switch
        {
            // 临时会话的用户不是服务对象，消息、撤回与置顶都不交付。
            MessageReceivedEvent { Message.SourceType: MessageSourceType.Temp } or MessageDeletedEvent { SourceType: MessageSourceType.Temp }
                or PeerPinChangedEvent { SourceType: MessageSourceType.Temp } => null,
            MessageReceivedEvent { Message.SourceType: MessageSourceType.Group } => Nb2EventKind.GroupMessage,
            MessageReceivedEvent => Nb2EventKind.PrivateMessage,
            FriendRequestEvent or GroupJoinRequestEvent or GroupInvitationEvent => Nb2EventKind.Request,
            MessageDeletedEvent or NudgeEvent or FileUploadEvent or GroupAdminChangedEvent or GroupEssenceChangedEvent or MemberJoinedEvent
                or MemberLeftEvent or GroupNameChangedEvent or GroupDisbandedEvent or GroupReactionEvent or GroupMuteEvent
                or PeerPinChangedEvent => Nb2EventKind.Notice,
            _ => null
        };

    /// <summary>重建 Milky 事件；没有对应 Milky 事件的类型为 null。</summary>
    /// <param name="e">Sora 事件。</param>
    /// <param name="key">本次交付的关联键。</param>
    public static JsonObject? Build(BotEvent e, string key)
    {
        (string Type, JsonObject Data)? payload = e switch
        {
            MessageReceivedEvent message => ("message_receive", Message(message)),
            MessageDeletedEvent recall => 
            ("message_recall", new JsonObject
            {
                ["message_scene"]  = Scene(recall.SourceType),
                ["peer_id"]        = recall.SourceType == MessageSourceType.Group ? (long)recall.GroupId : PrivatePeer(e, recall.SenderId, recall.OperatorId),
                ["sender_id"]      = (long)recall.SenderId,
                ["message_seq"]    = (long)recall.MessageId,
                ["operator_id"]    = (long)recall.OperatorId,
                ["display_suffix"] = recall.DisplaySuffix
            }),
            NudgeEvent { SourceType: MessageSourceType.Group } nudge => ("group_nudge", new JsonObject
            {
                ["group_id"]               = (long)nudge.GroupId,
                ["sender_id"]              = (long)nudge.SenderId,
                ["receiver_id"]            = (long)nudge.ReceiverId,
                ["display_action"]         = nudge.ActionText,
                ["display_suffix"]         = nudge.SuffixText,
                ["display_action_img_url"] = nudge.ActionImageUrl
            }),
            NudgeEvent nudge => ("friend_nudge", new JsonObject
            {
                ["user_id"]                = PrivatePeer(e, nudge.SenderId, nudge.ReceiverId),
                ["is_self_send"]           = (long)nudge.SenderId == (long)e.SelfId,
                ["is_self_receive"]        = (long)nudge.ReceiverId == (long)e.SelfId,
                ["display_action"]         = nudge.ActionText,
                ["display_suffix"]         = nudge.SuffixText,
                ["display_action_img_url"] = nudge.ActionImageUrl
            }),
            FileUploadEvent { SourceType: MessageSourceType.Group } upload => ("group_file_upload", new JsonObject
            {
                ["group_id"]  = (long)upload.GroupId,
                ["user_id"]   = (long)upload.UserId,
                ["file_id"]   = upload.FileId,
                ["file_name"] = upload.FileName,
                ["file_size"] = upload.FileSize
            }),
            FileUploadEvent upload => ("friend_file_upload", new JsonObject
            {
                ["user_id"]   = (long)upload.UserId,
                ["file_id"]   = upload.FileId,
                ["file_name"] = upload.FileName,
                ["file_size"] = upload.FileSize,
                ["file_hash"] = upload.FileHash,
                ["is_self"]   = upload.IsSelfSent
            }),
            GroupAdminChangedEvent admin => ("group_admin_change", new JsonObject
            {
                ["group_id"]    = (long)admin.GroupId,
                ["user_id"]     = (long)admin.UserId,
                ["operator_id"] = (long)admin.OperatorId,
                ["is_set"]      = admin.IsSet
            }),
            GroupEssenceChangedEvent essence => ("group_essence_message_change", new JsonObject
            {
                ["group_id"]    = (long)essence.GroupId,
                ["message_seq"] = (long)essence.MessageId,
                ["operator_id"] = (long)essence.OperatorId,
                ["is_set"]      = essence.IsSet
            }),
            MemberJoinedEvent joined => ("group_member_increase", new JsonObject
            {
                ["group_id"]    = (long)joined.GroupId,
                ["user_id"]     = (long)joined.UserId,
                ["operator_id"] = joined.OperatorId is { } joinOperator ? (long)joinOperator : null,
                ["invitor_id"]  = joined.InvitorId is { } invitor ? (long)invitor : null
            }),
            MemberLeftEvent left => ("group_member_decrease", new JsonObject
            {
                ["group_id"]    = (long)left.GroupId,
                ["user_id"]     = (long)left.UserId,
                ["operator_id"] = left.OperatorId is { } leftOperator ? (long)leftOperator : null
            }),
            GroupNameChangedEvent name => ("group_name_change", new JsonObject
            {
                ["group_id"]       = (long)name.GroupId,
                ["new_group_name"] = name.NewName,
                ["operator_id"]    = (long)name.OperatorId
            }),
            GroupDisbandedEvent disband => ("group_disband", new JsonObject
            {
                ["group_id"]    = (long)disband.GroupId,
                ["operator_id"] = (long)disband.OperatorId
            }),
            GroupReactionEvent reaction => ("group_message_reaction", new JsonObject
            {
                ["group_id"]      = (long)reaction.GroupId,
                ["user_id"]       = (long)reaction.UserId,
                ["message_seq"]   = (long)reaction.MessageId,
                ["face_id"]       = reaction.FaceId,
                ["reaction_type"] = reaction.ReactionType,
                ["is_add"]        = reaction.IsAdd
            }),
            GroupMuteEvent { IsWholeGroup: true } mute => ("group_whole_mute", new JsonObject
            {
                ["group_id"]    = (long)mute.GroupId,
                ["operator_id"] = (long)mute.OperatorId,
                ["is_mute"]     = mute.DurationSeconds != 0
            }),
            GroupMuteEvent mute => ("group_mute", new JsonObject
            {
                ["group_id"]    = (long)mute.GroupId,
                ["user_id"]     = (long)mute.UserId,
                ["duration"]    = mute.DurationSeconds,
                ["operator_id"] = (long)mute.OperatorId
            }),
            PeerPinChangedEvent pin => ("peer_pin_change", new JsonObject
            {
                ["message_scene"] = Scene(pin.SourceType),
                ["peer_id"]       = pin.PeerId,
                ["is_pinned"]     = pin.IsPinned
            }),
            FriendRequestEvent request => ("friend_request", new JsonObject
            {
                ["initiator_id"]  = (long)request.FromUserId,
                ["initiator_uid"] = string.Empty,
                ["comment"]       = request.Comment,
                ["via"]           = request.Via
            }),
            GroupJoinRequestEvent { JoinNotificationType: GroupJoinNotificationType.InvitedJoinRequest } invited => ("group_invited_join_request", new JsonObject
            {
                ["group_id"]         = (long)invited.GroupId,
                ["notification_seq"] = invited.NotificationSeq,
                ["initiator_id"]     = (long)invited.InvitorId,
                ["target_user_id"]   = (long)invited.FromUserId
            }),
            GroupJoinRequestEvent join => ("group_join_request", new JsonObject
            {
                ["group_id"]         = (long)join.GroupId,
                ["notification_seq"] = join.NotificationSeq,
                ["is_filtered"]      = join.IsFiltered,
                ["initiator_id"]     = (long)join.FromUserId,
                ["comment"]          = join.Comment
            }),
            GroupInvitationEvent invitation => ("group_invitation", new JsonObject
            {
                ["group_id"]        = (long)invitation.GroupId,
                ["invitation_seq"]  = invitation.InvitationSeq,
                ["initiator_id"]    = (long)invitation.InvitorId,
                ["source_group_id"] = (long)invitation.SourceGroupId == 0 ? null : (long)invitation.SourceGroupId
            }),
            _ => null
        };

        if (payload is not { } built) return null;
        return new JsonObject
        {
            ["time"]       = UnixSeconds(e.Time),
            ["self_id"]    = (long)e.SelfId,
            ["event_type"] = built.Type,
            ["data"]       = built.Data,
            [KEY_FIELD]    = key
        };
    }

    private static JsonObject Message(MessageReceivedEvent e)
    {
        bool group = e.Message.SourceType == MessageSourceType.Group;
        JsonObject data = new()
        {
            ["message_scene"] = Scene(e.Message.SourceType),
            ["peer_id"]       = group ? (long)e.Message.GroupId : (long)e.Message.SenderId,
            ["message_seq"]   = (long)e.Message.MessageId,
            ["sender_id"]     = (long)e.Message.SenderId,
            ["time"]          = UnixSeconds(e.Message.Time),
            ["segments"]      = MilkySegments.ToMilky(e.Message.Body)
        };
        if (!group && (long)e.Sender.UserId != 0) data["friend"] = Friend(e.Sender);
        if ((long)e.Group.GroupId != 0) data["group"] = Group(e.Group);
        if ((long)e.Member.UserId != 0) data["group_member"] = Member(e.Member);
        return data;
    }

    // Sora 的 UserInfo 只有账号、昵称与性别；Milky 好友另需 qid、备注与分组，NB2 插件不依赖这些字段。
    private static JsonObject Friend(UserInfo user) =>
        new()
        {
            ["user_id"]  = (long)user.UserId,
            ["nickname"] = user.Nickname,
            ["sex"]      = Sex(user.Sex),
            ["qid"]      = string.Empty,
            ["remark"]   = string.Empty,
            ["category"] = new JsonObject
            {
                ["category_id"]   = 0,
                ["category_name"] = string.Empty
            }
        };

    private static JsonObject Group(GroupInfo group) =>
        new()
        {
            ["group_id"]         = (long)group.GroupId,
            ["group_name"]       = group.GroupName,
            ["member_count"]     = group.MemberCount,
            ["max_member_count"] = group.MaxMemberCount,
            ["remark"]           = group.Remark,
            ["created_time"]     = group.CreatedTime,
            ["description"]      = group.Description,
            ["question"]         = group.Question,
            ["announcement"]     = group.Announcement
        };

    private static JsonObject Member(GroupMemberInfo member) =>
        new()
        {
            ["user_id"]          = (long)member.UserId,
            ["nickname"]         = member.Nickname,
            ["sex"]              = Sex(member.Sex),
            ["group_id"]         = (long)member.GroupId,
            ["card"]             = member.Card,
            ["title"]            = member.Title,
            ["level"]            = member.Level,
            ["role"]             = Role(member.Role),
            ["join_time"]        = member.JoinTime is { } joined ? UnixSeconds(joined) : 0,
            ["last_sent_time"]   = member.LastSentTime is { } sent ? UnixSeconds(sent) : 0,
            ["shut_up_end_time"] = member.MuteExpireTime is { } muted ? UnixSeconds(muted) : null
        };

    // 私聊事件只给出双方账号，对方即不是机器人自己的一方。
    private static long PrivatePeer(BotEvent e, long first, long second) => first == (long)e.SelfId ? second : first;

    private static string Scene(MessageSourceType type) =>
        type switch
        {
            MessageSourceType.Group => GROUP,
            _                       => FRIEND
        };

    private static string Sex(Sex sex) =>
        sex switch
        {
            Sora.Core.Enums.Sex.Male   => "male",
            Sora.Core.Enums.Sex.Female => "female",
            _                          => "unknown"
        };

    private static string Role(MemberRole role) =>
        role switch
        {
            MemberRole.Owner => "owner",
            MemberRole.Admin => "admin",
            _                => "member"
        };

    private static long UnixSeconds(DateTime time) => new DateTimeOffset(time).ToUnixTimeSeconds();
}
