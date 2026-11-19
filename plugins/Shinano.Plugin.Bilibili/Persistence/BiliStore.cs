using Shinano.Framework.Plugins;
using SqlSugar;

namespace Shinano.Plugin.Bilibili.Persistence;

/// <summary>
/// 当前 B站账号与按 B站账号区分的关注列表（全局），推送设置、推送进度与直播状态（按机器人账号）。推送设置按 UP 保存：
/// 换号后保留，新账号也关注的 UP 自动沿用；登录账号取关的 UP 重置为初始状态。只推送当前账号关注的 UP。线程安全。
/// </summary>
public sealed class BiliStore
{
    private readonly IPluginInitializationContext _context;

    /// <summary>创建存储。</summary>
    public BiliStore(IPluginInitializationContext context)
    {
        _context = context;
    }

    private ISqlSugarClient Global => _context.Database.Global;

    private ISqlSugarClient Account => _context.Database.Account;

    /// <summary>创建缺失的表。</summary>
    public void Initialize()
    {
        Global.CodeFirst.InitTables<BiliAccountRecord, BiliFollowRecord>();
        Account.CodeFirst.InitTables<BiliSubscriptionRecord, BiliTargetRecord, BiliLiveRecord>();
    }

    /// <summary>当前部署的 B站账号；没有登录过或已主动退出时为 null。</summary>
    public async Task<BiliAccountRecord?> AccountAsync() => await Global.Queryable<BiliAccountRecord>().FirstAsync();

    /// <summary>登录成功：保存为当前账号（替换其他账号），清除失效标记。</summary>
    public async Task SaveAccountAsync(BiliAccount account)
    {
        await Global.Deleteable<BiliAccountRecord>().Where(record => record.Uid != account.Uid).ExecuteCommandAsync();
        await Global.Storageable(
                        new BiliAccountRecord
                        {
                            Uid     = account.Uid,
                            Name    = account.Name,
                            Expired = false
                        })
                    .ExecuteCommandAsync();
    }

    /// <summary>标记当前账号已失效并已通知。</summary>
    public Task MarkExpiredAsync(long uid) =>
        Global.Updateable<BiliAccountRecord>().SetColumns(record => record.Expired == true).Where(record => record.Uid == uid).ExecuteCommandAsync();

    /// <summary>主动退出：删除当前账号记录；其关注列表与推送设置保留，重新登录该账号时恢复。</summary>
    public Task DeleteAccountAsync(long uid) => Global.Deleteable<BiliAccountRecord>().In(uid).ExecuteCommandAsync();

    /// <summary>一个 B站账号的全部关注，按 UID 排列。</summary>
    public Task<List<BiliFollowRecord>> FollowsAsync(long accountUid) =>
        Global.Queryable<BiliFollowRecord>().Where(follow => follow.AccountUid == accountUid).OrderBy(follow => follow.Uid).ToListAsync();

    /// <summary>
    /// 以 <paramref name="followings" /> 为准更新一个账号的关注列表：新增与改名的写入，该账号不再关注的删除；其他 B站账号的关注不受影响。
    /// 该账号取关的 UP，推送设置、推送的群、推送进度与直播状态一并删除（回到新关注的初始状态，避免换号后被另一个账号误推送）。
    /// 换号不是取关：原账号的记录与推送设置保留，新账号也关注的 UP 沿用；新出现在该账号列表中的 UP 若已有推送进度，
    /// 把进度推进到 <paramref name="now" />，避免把未关注期间的旧动态补推出去。
    /// </summary>
    /// <returns>被删除（已取关）的 UID。</returns>
    public async Task<IReadOnlyList<long>> ReplaceFollowsAsync(long accountUid, IReadOnlyList<BiliFollowing> followings, DateTimeOffset now)
    {
        Dictionary<long, BiliFollowRecord> existing = (await FollowsAsync(accountUid)).ToDictionary(follow => follow.Uid);
        List<BiliFollowRecord> upserts = [];
        foreach (BiliFollowing following in followings.DistinctBy(following => following.Uid))
        {
            BiliFollowRecord record = existing.GetValueOrDefault(following.Uid)
                                      ?? new BiliFollowRecord
                                      {
                                          AccountUid = accountUid,
                                          Uid        = following.Uid
                                      };
            record.Name = following.Name;
            record.Face = following.Face;
            upserts.Add(record);
        }

        HashSet<long> current = [.. followings.Select(following => following.Uid)];
        List<long> removed = [.. existing.Keys.Where(uid => !current.Contains(uid))];
        List<long> added = [.. current.Where(uid => !existing.ContainsKey(uid))];
        if (upserts.Count > 0) await Global.Storageable(upserts).ExecuteCommandAsync();
        if (removed.Count > 0)
        {
            await Global.Deleteable<BiliFollowRecord>().Where(follow => follow.AccountUid == accountUid && removed.Contains(follow.Uid)).ExecuteCommandAsync();
            await Account.Deleteable<BiliSubscriptionRecord>().In(removed).ExecuteCommandAsync();
            await Account.Deleteable<BiliTargetRecord>().Where(target => removed.Contains(target.Uid)).ExecuteCommandAsync();
            await Account.Deleteable<BiliLiveRecord>().In(removed).ExecuteCommandAsync();
        }

        if (added.Count > 0)
            await Account.Updateable<BiliTargetRecord>()
                         .SetColumns(target => new BiliTargetRecord
                         {
                             PushedAt = now.ToUnixTimeSeconds(),
                             PushedId = null
                         })
                         .Where(target => added.Contains(target.Uid) && target.PushedAt != null)
                         .ExecuteCommandAsync();

        return removed;
    }

    /// <summary>记录 UP 最近一条动态的发布时间（只会向后推进）。</summary>
    public Task RecordLatestDynamicAsync(long accountUid, long uid, DateTimeOffset publishedAt) =>
        Global.Updateable<BiliFollowRecord>()
              .SetColumns(follow => follow.LastDynamicAt == publishedAt)
              .Where(follow => follow.AccountUid == accountUid && follow.Uid == uid && (follow.LastDynamicAt == null || follow.LastDynamicAt < publishedAt))
              .ExecuteCommandAsync();

    /// <summary>当前账号的全部推送设置。</summary>
    public Task<List<BiliSubscriptionRecord>> SubscriptionsAsync() => Account.Queryable<BiliSubscriptionRecord>().ToListAsync();

    /// <summary>当前账号的全部推送目标。</summary>
    public Task<List<BiliTargetRecord>> TargetsAsync() => Account.Queryable<BiliTargetRecord>().ToListAsync();

    /// <summary>
    /// 保存一个 UP 的推送设置与推送的群：保留仍在其中的群的推送进度，移除的群删除进度，新增的群从头开始（首次只推最新一条）。
    /// 动态与直播都关闭时删除设置。
    /// </summary>
    public async Task SaveSubscriptionAsync(long uid, bool pushDynamic, bool pushLive, IReadOnlyCollection<long> groups)
    {
        HashSet<long> wanted = [.. groups];
        List<BiliTargetRecord> targets = await Account.Queryable<BiliTargetRecord>().Where(target => target.Uid == uid).ToListAsync();
        List<long> dropped = [.. targets.Select(target => target.GroupId).Where(group => !wanted.Contains(group))];
        List<BiliTargetRecord> added =
        [
            .. wanted.Where(group => targets.All(target => target.GroupId != group))
                     .Select(group => new BiliTargetRecord
                     {
                         Uid     = uid,
                         GroupId = group
                     })
        ];

        await Account.Ado.UseTranAsync(async () =>
        {
            if (dropped.Count > 0)
                await Account.Deleteable<BiliTargetRecord>().Where(target => target.Uid == uid && dropped.Contains(target.GroupId)).ExecuteCommandAsync();
            if (added.Count > 0) await Account.Insertable(added).ExecuteCommandAsync();
            if (pushDynamic || pushLive)
                await Account.Storageable(
                                 new BiliSubscriptionRecord
                                 {
                                     Uid         = uid,
                                     PushDynamic = pushDynamic,
                                     PushLive    = pushLive
                                 })
                             .ExecuteCommandAsync();
            else
                await Account.Deleteable<BiliSubscriptionRecord>().In(uid).ExecuteCommandAsync();
        });
    }

    /// <summary>推进一个群对一个 UP 的动态推送进度。</summary>
    public Task SavePushedAsync(long uid, long groupId, long pushedAt, string? pushedId) =>
        Account.Updateable<BiliTargetRecord>()
               .SetColumns(target => new BiliTargetRecord
               {
                   PushedAt = pushedAt,
                   PushedId = pushedId
               })
               .Where(target => target.Uid == uid && target.GroupId == groupId)
               .ExecuteCommandAsync();

    /// <summary>当前账号记录的直播状态。</summary>
    public Task<List<BiliLiveRecord>> LivesAsync() => Account.Queryable<BiliLiveRecord>().ToListAsync();

    /// <summary>保存直播状态。</summary>
    public Task SaveLiveAsync(BiliLiveRecord record) => Account.Storageable(record).ExecuteCommandAsync();
}
