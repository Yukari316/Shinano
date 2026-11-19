using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using SqlSugar;

namespace Shinano.Plugin.Hso.Persistence;

/// <summary>按账号隔离的群设置与发图计数。线程安全。</summary>
public sealed class HsoGroups
{
    private readonly IPluginInitializationContext _context;
    private readonly ILogger<HsoGroups>           _logger;

    /// <summary>创建存储。</summary>
    public HsoGroups(IPluginInitializationContext context, ILogger<HsoGroups> logger)
    {
        _context = context;
        _logger  = logger;
    }

    private ISqlSugarClient Database => _context.Database.Account;

    /// <summary>创建缺失的表。</summary>
    public void Initialize() => Database.CodeFirst.InitTables<HsoGroupSettingRecord, HsoUsageRecord>();

    /// <summary>群设置；没有记录时为不屏蔽、不允许 NSFW。</summary>
    public async Task<HsoGroupSettingRecord> SettingAsync(long groupId) =>
        await Database.Queryable<HsoGroupSettingRecord>().InSingleAsync(groupId)
        ?? new HsoGroupSettingRecord { GroupId = groupId };

    /// <summary>全部已保存的群设置。</summary>
    public Task<List<HsoGroupSettingRecord>> SettingsAsync() => Database.Queryable<HsoGroupSettingRecord>().ToListAsync();

    /// <summary>保存群设置。</summary>
    public async Task SaveSettingAsync(HsoGroupSettingRecord setting)
    {
        await Database.Storageable(setting).ExecuteCommandAsync();
        _logger.LogInformation("面板修改 Hso 群 {GroupId} 设置：屏蔽 {Blocked}，允许 NSFW {AllowNsfw}", setting.GroupId, setting.Blocked, setting.AllowNsfw);
    }

    /// <summary>成员的发图次数加一。</summary>
    public async Task IncrementAsync(long groupId, long userId)
    {
        // SQL_NEED: 并发触发时需要单条语句完成「插入或加一」，SqlSugar 不支持 on conflict ... do update 的自增表达式。
        await Database.Ado.ExecuteCommandAsync(
            "insert into hso_usage (group_id, user_id, count) values (@groupId, @userId, 1) "
            + "on conflict (group_id, user_id) do update set count = hso_usage.count + 1",
            new SugarParameter("@groupId", groupId),
            new SugarParameter("@userId", userId));
    }

    /// <summary>群内次数最多的成员，次数相同时按 QQ 号排列。</summary>
    public Task<List<HsoUsageRecord>> TopAsync(long groupId, int count) =>
        Database.Queryable<HsoUsageRecord>()
                .Where(record => record.GroupId == groupId && record.Count > 0)
                .OrderByDescending(record => record.Count)
                .OrderBy(record => record.UserId)
                .Take(count)
                .ToListAsync();

    /// <summary>群内上榜（次数大于 0）的人数。</summary>
    public Task<int> RankedCountAsync(long groupId) =>
        Database.Queryable<HsoUsageRecord>().Where(record => record.GroupId == groupId && record.Count > 0).CountAsync();
}
