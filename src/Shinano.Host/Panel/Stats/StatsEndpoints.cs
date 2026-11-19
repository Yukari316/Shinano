using Shinano.Host.Bot;
using Shinano.Infrastructure.ChatArchive;
using Shinano.Infrastructure.ChatArchive.Persistence;
using Shinano.Infrastructure.Hosting;
using Sora.Entities.Interfaces;

namespace Shinano.Host.Panel.Stats;

/// <summary>
/// 发言统计与归档缺口。日期按配置时区计算；群名、好友备注与群名片通过 Milky 实时获取，取不到时只返回 ID。
/// </summary>
internal static class StatsEndpoints
{
    private const int DEFAULT_DAYS  = 30;
    private const int MAX_DAYS      = 366;
    private const int DEFAULT_LIMIT = 20;
    private const int MAX_LIMIT     = 100;


    internal static async Task<IResult> DailyAsync(
        HostInfrastructure infrastructure,
        BotAccessor        bot,
        CancellationToken  cancellationToken,
        int                days           = DEFAULT_DAYS,
        ChatStatScope      scope          = ChatStatScope.All,
        long?              conversationId = null,
        bool               includeBot     = false)
    {
        if (Unavailable(infrastructure, bot) is { } unavailable) return unavailable;
        ChatStatFilter filter = CreateFilter(infrastructure, bot, days, scope, conversationId, includeBot);

        List<ChatStatTotals> rows = await new ChatStatsQuery(infrastructure).DailyAsync(filter, cancellationToken);
        return Results.Ok(
            rows.Select(row => new
            {
                date     = DateOnly.FromDateTime(row.Date),
                messages = row.Messages,
                chars    = row.Chars,
                images   = row.Images,
                audios   = row.Audios,
                videos   = row.Videos,
                files    = row.Files
            }));
    }

    internal static async Task<IResult> ConversationsAsync(
        HostInfrastructure infrastructure,
        BotAccessor        bot,
        CancellationToken  cancellationToken,
        int                days       = DEFAULT_DAYS,
        ChatStatScope      scope      = ChatStatScope.Group,
        bool               includeBot = false)
    {
        if (Unavailable(infrastructure, bot) is { } unavailable) return unavailable;
        ChatStatFilter filter = CreateFilter(infrastructure, bot, days, scope, null, includeBot);

        List<ChatStatTotals> rows = await new ChatStatsQuery(infrastructure).ConversationsAsync(filter, cancellationToken);
        Dictionary<long, string> groups  = await GroupNamesAsync(bot.Api, cancellationToken);
        Dictionary<long, string> friends = await FriendNamesAsync(bot.Api, cancellationToken);
        return Results.Ok(
            rows.Select(row =>
            {
                bool isGroup = row.ConversationType == (short)ChatConversationType.Group;
                return new
                {
                    type           = (ChatConversationType)row.ConversationType,
                    conversationId = row.ConversationId,
                    name           = (isGroup ? groups : friends).GetValueOrDefault(row.ConversationId),
                    senders        = row.Senders,
                    messages       = row.Messages,
                    chars          = row.Chars,
                    images         = row.Images,
                    audios         = row.Audios,
                    videos         = row.Videos,
                    files          = row.Files
                };
            }));
    }

    internal static async Task<IResult> SendersAsync(
        HostInfrastructure infrastructure,
        BotAccessor        bot,
        CancellationToken  cancellationToken,
        int                days           = DEFAULT_DAYS,
        ChatStatScope      scope          = ChatStatScope.All,
        long?              conversationId = null,
        bool               includeBot     = false,
        int                limit          = DEFAULT_LIMIT)
    {
        if (Unavailable(infrastructure, bot) is { } unavailable) return unavailable;
        ChatStatFilter filter = CreateFilter(infrastructure, bot, days, scope, conversationId, includeBot);

        List<ChatStatTotals> rows = await new ChatStatsQuery(infrastructure).SendersAsync(
            filter,
            Math.Clamp(limit, 1, MAX_LIMIT),
            cancellationToken);
        // 群内排行使用群名片；其余只用好友列表补名称，避免逐个查询用户资料（协议端可能很慢）。
        Dictionary<long, string> names = scope == ChatStatScope.Group && conversationId is { } groupId
            ? await MemberNamesAsync(bot.Api, groupId, cancellationToken)
            : await FriendNamesAsync(bot.Api, cancellationToken);
        return Results.Ok(
            rows.Select(row => new
            {
                senderId = row.SenderId,
                name     = names.GetValueOrDefault(row.SenderId),
                isBot    = row.SenderId == filter.SelfId,
                messages = row.Messages,
                chars    = row.Chars,
                images   = row.Images,
                audios   = row.Audios,
                videos   = row.Videos,
                files    = row.Files
            }));
    }

    internal static async Task<IResult> GapsAsync(
        HostInfrastructure infrastructure,
        BotAccessor        bot,
        CancellationToken  cancellationToken,
        int                limit = MAX_LIMIT)
    {
        if (Unavailable(infrastructure, bot) is { } unavailable) return unavailable;

        List<ArchiveGapRecord> gaps = await new ChatStatsQuery(infrastructure).GapsAsync(
            bot.SelfId,
            Math.Clamp(limit, 1, MAX_LIMIT),
            cancellationToken);
        return Results.Ok(
            gaps.Select(gap => new
            {
                cause     = (ArchiveGapCause)gap.Cause,
                startedAt = gap.StartedAt,
                endedAt   = gap.EndedAt
            }));
    }

    // 统计属于当前机器人账号：基础设施未就绪或进程启动后尚未连接过 LLBot 时没有可查询的数据。
    private static IResult? Unavailable(HostInfrastructure infrastructure, BotAccessor bot)
    {
        if (!PanelResults.IsReady(infrastructure)) return PanelResults.Unavailable();
        return bot.SelfId == 0
            ? PanelResults.Error(StatusCodes.Status503ServiceUnavailable, "尚未连接机器人账号，暂无统计数据")
            : null;
    }

    private static ChatStatFilter CreateFilter(
        HostInfrastructure infrastructure,
        BotAccessor        bot,
        int                days,
        ChatStatScope      scope,
        long?              conversationId,
        bool               includeBot)
    {
        DateOnly today = ChatPartitionMaintainer.LocalDate(DateTimeOffset.UtcNow, infrastructure.TimeZone);
        return new ChatStatFilter
        {
            SelfId         = bot.SelfId,
            From           = today.AddDays(1 - Math.Clamp(days, 1, MAX_DAYS)),
            To             = today,
            Scope          = scope,
            ConversationId = conversationId,
            IncludeBot     = includeBot
        };
    }

    private static async Task<Dictionary<long, string>> GroupNamesAsync(IBotApi? api, CancellationToken cancellationToken) =>
        api is not null && await api.GetGroupListAsync(false, cancellationToken) is { IsSuccess: true, Data: { } groups }
            ? groups.ToDictionary(group => (long)group.GroupId, group => group.GroupName)
            : [];

    private static async Task<Dictionary<long, string>> FriendNamesAsync(IBotApi? api, CancellationToken cancellationToken) =>
        api is not null && await api.GetFriendListAsync(false, cancellationToken) is { IsSuccess: true, Data: { } friends }
            ? friends.ToDictionary(
                friend => (long)friend.UserId,
                friend => string.IsNullOrEmpty(friend.Remark) ? friend.Nickname : friend.Remark)
            : [];

    private static async Task<Dictionary<long, string>> MemberNamesAsync(
        IBotApi?          api,
        long              groupId,
        CancellationToken cancellationToken) =>
        api is not null && await api.GetGroupMemberListAsync(groupId, false, cancellationToken) is { IsSuccess: true, Data: { } members }
            ? members.ToDictionary(
                member => (long)member.UserId,
                member => string.IsNullOrEmpty(member.Card) ? member.Nickname : member.Card)
            : [];
}
