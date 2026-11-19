using System.Text.Json;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili.Login;
using Shinano.Plugin.Bilibili.Persistence;

namespace Shinano.Plugin.Bilibili.Feed;

/// <summary>
/// 按配置的间隔轮询直播状态与关注流并推送，另负责关注列表刷新与浏览器维护。直播不需要登录；动态需要登录，
/// 遇到风控时暂停 1 小时，暂停结束后先经浏览器刷新登录信息再读取。各任务由宿主调度器调用，同一任务不重入。
/// </summary>
public sealed class BiliPoller
{
    /// <summary>调度器调用 <see cref="PollAsync" /> 的间隔；实际轮询间隔由配置决定。</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    /// <summary>关注列表的刷新间隔。</summary>
    public static readonly TimeSpan FollowsInterval = TimeSpan.FromHours(1);

    /// <summary>浏览器维护的间隔。</summary>
    public static readonly TimeSpan MaintenanceInterval = TimeSpan.FromDays(1);

    private const int MAX_FEED_PAGES = 3;

    // 调度器的触发时刻有少许抖动，留出余量避免 10 分钟的间隔被推迟到 11 分钟。
    private static readonly TimeSpan IntervalSlack = TimeSpan.FromSeconds(30);

    private readonly IPluginInitializationContext _context;
    private readonly BiliHttp                     _http;
    private readonly BiliSession                  _session;
    private readonly BiliStore                    _store;
    private readonly BiliCardRenderer             _renderer;
    private readonly BiliPusher                   _pusher;
    private readonly TimeProvider                 _time;
    private readonly ILogger<BiliPoller>          _logger;
    // 上次轮询时刻的 UTC Ticks；0 表示下一次调度即轮询。调度线程与面板请求都会访问，用原子操作读写。
    private          long                         _lastPollTicks;
    private          string?                      _baseline;

    /// <summary>创建轮询器。</summary>
    public BiliPoller(
        IPluginInitializationContext context,
        BiliHttp                     http,
        BiliSession                  session,
        BiliStore                    store,
        BiliCardRenderer             renderer,
        BiliPusher                   pusher,
        TimeProvider                 time,
        ILogger<BiliPoller>          logger)
    {
        _context  = context;
        _http     = http;
        _session  = session;
        _store    = store;
        _renderer = renderer;
        _pusher   = pusher;
        _time     = time;
        _logger   = logger;
    }

    /// <summary>浏览器维护：网页前端检查与刷新登录后导出 Cookie，随后刷新关注列表。</summary>
    public async Task MaintainAsync(CancellationToken cancellationToken)
    {
        await _session.MaintainAsync(cancellationToken);
        await RefreshFollowsAsync(cancellationToken);
    }

    /// <summary>以当前登录账号的 B站关注列表为准刷新本地记录；未登录（含登录失效）时不做任何事。</summary>
    /// <returns>是否已刷新。</returns>
    public async Task<bool> RefreshFollowsAsync(CancellationToken cancellationToken)
    {
        if (_session.State != BiliLoginState.LoggedIn || _session.Account is not { } account) return false;
        try
        {
            IReadOnlyList<BiliFollowing> followings = await _http.FollowingsAsync(account.Uid, cancellationToken);
            IReadOnlyList<long> removed = await _store.ReplaceFollowsAsync(account.Uid, followings, _time.GetUtcNow());
            _logger.LogInformation("已刷新 B站关注列表：{Count} 个 UP，取关 {Removed}", followings.Count, removed.Count == 0 ? "无" : string.Join("、", removed));
            return true;
        }
        catch (Exception exception) when (exception is BiliApiException or HttpRequestException or JsonException or TaskCanceledException
                                          && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "刷新 B站关注列表失败");
            return false;
        }
    }

    /// <summary>让下一次调度（1 分钟内）立即轮询，用于登录成功与修改推送设置之后，不必等满轮询间隔。</summary>
    public void RequestPoll() => Interlocked.Exchange(ref _lastPollTicks, 0);

    /// <summary>
    /// 每分钟由调度器调用，到达配置的轮询间隔时检查直播与动态。推送范围为当前 B站账号的关注：登录失效时仍按失效前账号的关注推送直播，
    /// 未登录或已主动退出时不推送。
    /// </summary>
    public async Task PollAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = _time.GetUtcNow();
        TimeSpan interval = TimeSpan.FromMinutes(_context.Config<BiliConfig>().Value.PollIntervalMinutes);
        long last = Interlocked.Read(ref _lastPollTicks);
        if (now.UtcTicks - last < (interval - IntervalSlack).Ticks || _context.Bot.Api is null || _session.Account is not { } account) return;
        Interlocked.Exchange(ref _lastPollTicks, now.UtcTicks);

        Dictionary<long, BiliFollowRecord> follows = (await _store.FollowsAsync(account.Uid)).ToDictionary(follow => follow.Uid);
        List<BiliSubscriptionRecord> subscriptions = [.. (await _store.SubscriptionsAsync()).Where(subscription => follows.ContainsKey(subscription.Uid))];
        ILookup<long, BiliTargetRecord> targets = (await _store.TargetsAsync()).ToLookup(target => target.Uid);
        long[] liveUps = [.. subscriptions.Where(subscription => subscription.PushLive && targets.Contains(subscription.Uid)).Select(subscription => subscription.Uid)];
        long[] dynamicUps = [.. subscriptions.Where(subscription => subscription.PushDynamic && targets.Contains(subscription.Uid)).Select(subscription => subscription.Uid)];

        if (liveUps.Length > 0 || dynamicUps.Length > 0)
            _logger.LogInformation("B站轮询：动态 {Dynamic} 个 UP，直播 {Live} 个 UP（{State}）", dynamicUps.Length, liveUps.Length, _session.State);
        await PollLiveAsync(liveUps, follows, targets, cancellationToken);
        await PollDynamicsAsync(account.Uid, dynamicUps, follows, targets, cancellationToken);
    }

    private async Task PollLiveAsync(
        long[]                             uids,
        Dictionary<long, BiliFollowRecord> follows,
        ILookup<long, BiliTargetRecord>    targets,
        CancellationToken                  cancellationToken)
    {
        if (uids.Length == 0) return;
        IReadOnlyList<BiliLiveStatus> statuses;
        try
        {
            statuses = await _http.LiveStatusAsync(uids, cancellationToken);
        }
        catch (Exception exception) when (exception is BiliApiException or HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                              or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(exception, "查询 B站直播状态失败，本轮跳过");
            return;
        }

        Dictionary<long, BiliLiveRecord> records = (await _store.LivesAsync()).ToDictionary(record => record.Uid);
        DateTimeOffset now = _time.GetUtcNow();
        foreach (BiliLiveStatus status in statuses)
        {
            BiliLiveRecord? previous = records.GetValueOrDefault(status.Uid);
            string name = follows.TryGetValue(status.Uid, out BiliFollowRecord? follow) ? follow.Name : status.Uid.ToString();
            switch (BiliRules.Change(previous, status))
            {
                case BiliLiveChange.Started:
                    string started = ReplyTexts.Format(ReplyTexts.LIVE_STARTED, name, status.RoomId);
                    foreach (BiliTargetRecord target in targets[status.Uid]) await _pusher.PushTextAsync(target.GroupId, started);
                    await _store.SaveLiveAsync(Live(status.Uid, true, status.LiveSince ?? now));
                    break;
                case BiliLiveChange.Ended:
                    TimeSpan duration = previous?.LiveSince is { } since ? now - since : TimeSpan.Zero;
                    string ended = ReplyTexts.Format(ReplyTexts.LIVE_ENDED, name, ReplyTexts.Duration(duration));
                    foreach (BiliTargetRecord target in targets[status.Uid]) await _pusher.PushTextAsync(target.GroupId, ended);
                    await _store.SaveLiveAsync(Live(status.Uid, false, null));
                    break;
                default:
                    if (previous is null) await _store.SaveLiveAsync(Live(status.Uid, status.Live, status.LiveSince));
                    break;
            }
        }
    }

    private static BiliLiveRecord Live(long uid, bool live, DateTimeOffset? since) =>
        new()
        {
            Uid       = uid,
            Live      = live,
            LiveSince = since
        };

    private async Task PollDynamicsAsync(
        long                               accountUid,
        long[]                             uids,
        Dictionary<long, BiliFollowRecord> follows,
        ILookup<long, BiliTargetRecord>    targets,
        CancellationToken                  cancellationToken)
    {
        if (uids.Length == 0 || !await ReadyAsync(cancellationToken)) return;

        try
        {
            List<BiliTargetRecord> watched = [.. uids.SelectMany(uid => targets[uid])];
            bool initializing = watched.Any(target => target.PushedAt is null);
            if (!initializing && _baseline is { } baseline && await _http.FeedUpdateCountAsync(baseline, cancellationToken) == 0)
            {
                _session.ReportRecovered();
                return;
            }

            long oldest = watched.Where(target => target.PushedAt > 0).Select(target => target.PushedAt ?? 0).DefaultIfEmpty(0).Min();
            IReadOnlyList<BiliDynamic> feed = await ReadFeedAsync(oldest, cancellationToken);
            _session.ReportRecovered();

            Dictionary<string, BiliCard?> cards = [];
            foreach (long uid in uids)
            {
                List<BiliDynamic> own = [.. feed.Where(dynamic => dynamic.AuthorId == uid)];
                if (BiliRules.Latest(own) is { } latest) await _store.RecordLatestDynamicAsync(accountUid, uid, latest.PublishedAt);
                string name = follows[uid].Name;
                foreach (BiliTargetRecord target in targets[uid])
                    await PushAsync(uid, name, target, own, cards, cancellationToken);
            }
        }
        catch (BiliApiException exception) when (exception.IsRiskControl)
        {
            _session.ReportRisk(exception);
        }
        catch (BiliApiException exception) when (exception.IsNotLoggedIn)
        {
            _logger.LogWarning("读取 B站关注流时返回未登录，经浏览器重新确认登录状态");
            await _session.MaintainAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is BiliApiException or HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException
                                              or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(exception, "读取 B站关注流失败，本轮跳过");
        }
    }

    // 已登录且不在风控暂停中才读取；暂停结束后先经浏览器刷新登录信息与 Cookie。
    private async Task<bool> ReadyAsync(CancellationToken cancellationToken)
    {
        if (_session.RiskUntil is { } until)
        {
            if (_time.GetUtcNow() < until) return false;
            _logger.LogInformation("B站风控暂停已结束，刷新登录信息后重试");
            await _session.MaintainAsync(cancellationToken);
        }

        return _session.State == BiliLoginState.LoggedIn;
    }

    // 读到比最旧的推送进度更早的动态为止，最多 3 页；新订阅只需要第一页（或该 UP 的动态空间）。
    private async Task<IReadOnlyList<BiliDynamic>> ReadFeedAsync(long oldest, CancellationToken cancellationToken)
    {
        BiliFeedPage page = await _http.FeedAsync(null, cancellationToken);
        _baseline = page.UpdateBaseline ?? _baseline;
        List<BiliDynamic> items = [.. page.Items];
        for (int count = 1; count < MAX_FEED_PAGES && page.NextOffset is { } offset; count++)
        {
            if (items.Count > 0 && items.Min(dynamic => dynamic.PublishedAt.ToUnixTimeSeconds()) <= oldest) break;
            page = await _http.FeedAsync(offset, cancellationToken);
            items.AddRange(page.Items);
        }

        return items;
    }

    private async Task PushAsync(
        long                          uid,
        string                        name,
        BiliTargetRecord              target,
        List<BiliDynamic>             own,
        Dictionary<string, BiliCard?> cards,
        CancellationToken             cancellationToken)
    {
        IReadOnlyList<BiliDynamic> pending;
        if (target.PushedAt is not { } pushedAt)
        {
            // 新订阅只推当前最新一条；关注流中没有时读该 UP 的动态空间，仍没有则记为已初始化（0），之后照常推新动态。
            BiliDynamic? latest = BiliRules.Latest(own) ?? BiliRules.Latest((await _http.SpaceAsync(uid, cancellationToken)).Items);
            if (latest is null)
            {
                _logger.LogInformation("UP {Name}（{Uid}）没有可推送的动态，群 {GroupId} 从之后的新动态开始推送", name, uid, target.GroupId);
                await _store.SavePushedAsync(uid, target.GroupId, 0, null);
                return;
            }

            pending = [latest];
        }
        else
        {
            pending = BiliRules.NewSince(own, pushedAt, target.PushedId, out int skipped);
            if (skipped > 0)
                _logger.LogWarning("UP {Name}（{Uid}）本轮新动态超过 {Max} 条，群 {GroupId} 跳过较早的 {Skipped} 条", name, uid, BiliRules.MAX_PER_ROUND, target.GroupId, skipped);
        }

        if (pending.Count == 0) return;
        List<(BiliDynamic Dynamic, BiliCard? Card)> items = [];
        foreach (BiliDynamic dynamic in pending)
            items.Add((dynamic, await CardAsync(dynamic, cards, cancellationToken)));

        if (!await _pusher.PushDynamicsAsync(target.GroupId, name, items)) return;
        BiliDynamic last = pending[^1];
        await _store.SavePushedAsync(uid, target.GroupId, last.PublishedAt.ToUnixTimeSeconds(), last.Id);
    }

    // 同一条动态推给多个群时只绘制一次；绘制失败时为 null，改发文字。
    private async Task<BiliCard?> CardAsync(BiliDynamic dynamic, Dictionary<string, BiliCard?> cards, CancellationToken cancellationToken)
    {
        if (cards.TryGetValue(dynamic.Id, out BiliCard? cached)) return cached;
        BiliCard? card;
        try
        {
            card = await _renderer.RenderAsync(dynamic, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogError(exception, "绘制动态 {Id} 的卡片失败，改发文字", dynamic.Id);
            card = null;
        }

        cards[dynamic.Id] = card;
        return card;
    }
}
