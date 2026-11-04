using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Entertainment.Models;
using Sora.Command.Attributes;
using Sora.Core.Enums;
using Sora.Core.Types;
using Sora.Entities.Events;
using Sora.Entities.Info;
using Sora.Entities.Message;
using Sora.Entities.Segments;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.Entertainment;

/// <summary>骰子、随机选择与娱乐禁言。</summary>
[CommandGroup(Name = "entertainment")]
public sealed class EntertainmentCommands
{
    private const int    DICE_MIN           = 1;
    private const int    DICE_MAX_EXCLUSIVE = 7;
    private const int    PERCENT            = 100;
    private const int    SECONDS_PER_MINUTE = 60;
    private const string TAKE_ALL           = "我全都要";
    private const string MUTE_FAILED        = "我没有权限让你睡觉";

    private readonly IPluginConfig<EntertainmentConfig> _config;
    private readonly ILogger<EntertainmentCommands>     _logger;

    /// <summary>创建指令组。</summary>
    public EntertainmentCommands(IPluginInitializationContext context, ILogger<EntertainmentCommands> logger)
    {
        _config = context.Config<EntertainmentConfig>();
        _logger = logger;
    }

    /// <summary>丢一个六面骰子；群聊中 @ 发送者。</summary>
    [Command(
        Expressions = ["dice"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        Description = "丢骰子")]
    public async ValueTask DiceAsync(MessageReceivedEvent e)
    {
        string result = $"丢出了\r\n{Random.Shared.Next(DICE_MIN, DICE_MAX_EXCLUSIVE)}";
        MessageBody reply = e.Message.SourceType == MessageSourceType.Group
            ? SegmentBuilder.Mention(e.Message.SenderId) + result
            : new MessageBody(result);
        await e.ReplyAsync(reply);
    }

    /// <summary>吃什么</summary>
    [Command(
        Expressions = [".*吃什么.*"],
        MatchType = MatchType.Regex,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        Description = "是啊，吃什么")]
    [Cooldown(10)]
    public async ValueTask Eat(MessageReceivedEvent e)
    {
        await e.ReplyAsync("是啊，吃什么");
    }

    /// <summary>
    /// 从"选择A还是B"的选项中随机选一项，选项可以含图片与 QQ 表情。指令只按文字粗略匹配，不是有效选择时不回复，
    /// 也不占用这条消息，后续指令照常处理。
    /// </summary>
    [Command(
        Expressions = [$@"^{ChoiceParser.PREFIX}[\s\S]*{ChoiceParser.SEPARATOR}"],
        MatchType = MatchType.Regex,
        SourceScope = MessageSourceScope.Group | MessageSourceScope.Friend,
        BlockAfterMatch = false,
        Description = "随机选择")]
    public async ValueTask ChooseAsync(MessageReceivedEvent e)
    {
        if (ChoiceParser.Parse(e.Message.Body) is not { } options) return;
        e.IsContinueEventChain = false;

        MessageBody reply = new("你的选项有：");
        for (int index = 0; index < options.Count; index++)
        {
            reply.Add(SegmentBuilder.Text($"\n{index + 1}、"));
            foreach (Segment segment in options[index])
                reply.Add(segment);
        }

        reply.Add(SegmentBuilder.Text("\n建议你选择："));
        if (Random.Shared.Next(PERCENT) < _config.Value.TakeAllPercent)
            reply.Add(SegmentBuilder.Text(TAKE_ALL));
        else
            foreach (Segment segment in options[Random.Shared.Next(options.Count)])
                reply.Add(segment);
        await e.ReplyAsync(reply);
    }

    /// <summary>
    /// 把发送者禁言配置的时长。LLBot 对没有权限的禁言同样返回成功，因此先按 Bot 与发送者的群角色判断，没有权限时回复说明。
    /// </summary>
    [Command(
        Expressions = ["优质睡眠", "昏睡红茶", "昏睡套餐", "健康睡眠"],
        MatchType = MatchType.Full,
        SourceScope = MessageSourceScope.Group,
        Description = "娱乐禁言")]
    public async ValueTask SleepAsync(MessageReceivedEvent e)
    {
        // 角色可能刚被调整，不使用缓存。
        ApiResult<GroupMemberInfo> self = await e.Api.GetGroupMemberInfoAsync(e.Message.GroupId, e.SelfId, noCache: true);
        if (!self.IsSuccess || self.Data is not { } bot)
        {
            _logger.LogWarning("娱乐禁言：无法取得 Bot 在群 {GroupId} 的角色：{Code} {Message}", e.Message.GroupId, self.Code, self.Message);
            await e.ReplyAsync(new MessageBody(MUTE_FAILED));
            return;
        }

        if (!MutePermission.CanMute(bot.Role, e.Member.Role))
        {
            await e.ReplyAsync(new MessageBody(MUTE_FAILED));
            return;
        }

        ApiResult result = await e.Api.MuteGroupMemberAsync(
            e.Message.GroupId,
            e.Message.SenderId,
            _config.Value.MuteMinutes * SECONDS_PER_MINUTE);
        if (result.IsSuccess) return;

        _logger.LogWarning(
            "娱乐禁言失败：群 {GroupId} 成员 {UserId}，{Code} {Message}",
            e.Message.GroupId,
            e.Message.SenderId,
            result.Code,
            result.Message);
        await e.ReplyAsync(new MessageBody(MUTE_FAILED));
    }
}
