using Shinano.Host.Panel.Models;
using Shinano.Infrastructure.ChatArchive.Entities;
using Shinano.Infrastructure.ChatArchive.Models;
using Shinano.Infrastructure.Hosting;
using SqlSugar;

namespace Shinano.Host.Panel;

/// <summary>发言统计的聚合查询，数据来自当前机器人账号的 <c>chat_stat_daily</c>。</summary>
internal sealed class ChatStatsQuery
{
    private readonly HostInfrastructure _infrastructure;

    /// <summary>创建查询。</summary>
    public ChatStatsQuery(HostInfrastructure infrastructure)
    {
        _infrastructure = infrastructure;
    }

    /// <summary>按日汇总。</summary>
    public Task<List<ChatStatTotals>> DailyAsync(ChatStatFilter filter, CancellationToken cancellationToken) =>
        Filtered(filter)
            .GroupBy(stat => stat.StatDate)
            .OrderBy(stat => stat.StatDate)
            .Select(stat => new ChatStatTotals
            {
                Date     = SqlFunc.ToDate(stat.StatDate),
                Messages = SqlFunc.AggregateSum(stat.MessageCount),
                Chars    = SqlFunc.AggregateSum(stat.CharCount),
                Images   = SqlFunc.AggregateSum(stat.ImageCount),
                Audios   = SqlFunc.AggregateSum(stat.AudioCount),
                Videos   = SqlFunc.AggregateSum(stat.VideoCount),
                Files    = SqlFunc.AggregateSum(stat.FileCount)
            })
            .ToListAsync(cancellationToken);

    /// <summary>按会话汇总，按消息数降序。</summary>
    public Task<List<ChatStatTotals>> ConversationsAsync(ChatStatFilter filter, CancellationToken cancellationToken) =>
        Filtered(filter)
            .GroupBy(stat => new { stat.ConversationType, stat.ConversationId })
            .Select(stat => new ChatStatTotals
            {
                ConversationType = stat.ConversationType,
                ConversationId   = stat.ConversationId,
                Senders          = SqlFunc.AggregateDistinctCount(stat.SenderId),
                Messages         = SqlFunc.AggregateSum(stat.MessageCount),
                Chars            = SqlFunc.AggregateSum(stat.CharCount),
                Images           = SqlFunc.AggregateSum(stat.ImageCount),
                Audios           = SqlFunc.AggregateSum(stat.AudioCount),
                Videos           = SqlFunc.AggregateSum(stat.VideoCount),
                Files            = SqlFunc.AggregateSum(stat.FileCount)
            })
            .MergeTable()
            .OrderByDescending(totals => totals.Messages)
            .ToListAsync(cancellationToken);

    /// <summary>按发言者汇总，按消息数降序取前 <paramref name="limit" /> 名。</summary>
    public Task<List<ChatStatTotals>> SendersAsync(ChatStatFilter filter, int limit, CancellationToken cancellationToken) =>
        Filtered(filter)
            .GroupBy(stat => stat.SenderId)
            .Select(stat => new ChatStatTotals
            {
                SenderId = stat.SenderId,
                Messages = SqlFunc.AggregateSum(stat.MessageCount),
                Chars    = SqlFunc.AggregateSum(stat.CharCount),
                Images   = SqlFunc.AggregateSum(stat.ImageCount),
                Audios   = SqlFunc.AggregateSum(stat.AudioCount),
                Videos   = SqlFunc.AggregateSum(stat.VideoCount),
                Files    = SqlFunc.AggregateSum(stat.FileCount)
            })
            .MergeTable()
            .OrderByDescending(totals => totals.Messages)
            .Take(limit)
            .ToListAsync(cancellationToken);

    /// <summary>最近的归档缺口，按开始时间降序。</summary>
    public Task<List<ArchiveGapRecord>> GapsAsync(long selfId, int limit, CancellationToken cancellationToken) =>
        _infrastructure.Database.Account(selfId)
                       .Queryable<ArchiveGapRecord>()
                       .OrderByDescending(gap => gap.StartedAt)
                       .Take(limit)
                       .ToListAsync(cancellationToken);

    private ISugarQueryable<ChatStatDailyRecord> Filtered(ChatStatFilter filter)
    {
        DateOnly from = filter.From;
        DateOnly to   = filter.To;
        long selfId   = filter.SelfId;
        short group   = (short)ChatConversationType.Group;
        long conversation = filter.ConversationId ?? 0;
        return _infrastructure.Database.Account(filter.SelfId)
                              .Queryable<ChatStatDailyRecord>()
                              .Where(stat => stat.StatDate >= from && stat.StatDate <= to)
                              .WhereIF(!filter.IncludeBot, stat => stat.SenderId != selfId)
                              .WhereIF(filter.Scope == ChatStatScope.Group, stat => stat.ConversationType == group)
                              .WhereIF(filter.Scope == ChatStatScope.Private, stat => stat.ConversationType != group)
                              .WhereIF(filter.ConversationId is not null, stat => stat.ConversationId == conversation);
    }
}
