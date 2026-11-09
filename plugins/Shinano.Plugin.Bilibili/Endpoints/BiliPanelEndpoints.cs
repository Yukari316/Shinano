using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Shinano.Framework.Plugins;
using Shinano.Plugin.Bilibili.Entities;
using Shinano.Plugin.Bilibili.Models;
using Sora.Entities.Info;

namespace Shinano.Plugin.Bilibili.Endpoints;

/// <summary>B站订阅专有面板 API 的处理方法，路由见 <see cref="BiliPanelApi" />。</summary>
internal static class BiliPanelEndpoints
{
    private const string PNG_DATA_URL = "data:image/png;base64,";

    /// <summary>登录状态。</summary>
    internal static IResult Status(BiliSession session) =>
        Results.Ok(
            new BiliStatusView
            {
                State     = session.State,
                Account   = session.Account,
                RiskUntil = session.RiskUntil
            });

    /// <summary>
    /// 生成扫码登录的二维码。已登录且未确认退出时返回 409 与原账号；确认后先退出原账号（服务端失效并清空浏览器数据）。
    /// 登录已失效时不需要确认，直接清空原账号的登录数据。
    /// </summary>
    internal static async Task<IResult> StartLoginAsync(BiliSession session, BiliLogin login, BiliLoginRequest request, ILogger logger, CancellationToken cancellationToken)
    {
        if (session.Account is { } account)
        {
            if (session.State == BiliLoginState.LoggedIn && !request.ConfirmLogout) return Results.Conflict(account);
            logger.LogInformation("面板重新登录，先退出 B站账号 {Uid}（{State}）", account.Uid, session.State);
            await session.LogoutAsync(cancellationToken);
        }

        try
        {
            BiliQrCode qr = await login.StartAsync(cancellationToken);
            return Results.Ok(
                new BiliQrView
                {
                    Key   = qr.Key,
                    Image = PNG_DATA_URL + Convert.ToBase64String(qr.Png)
                });
        }
        catch (Exception exception) when (exception is BiliApiException or HttpRequestException)
        {
            logger.LogError(exception, "面板生成 B站登录二维码失败");
            return Results.Problem("生成二维码失败，详见日志", statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>查询扫码进度；成功时已写入浏览器。</summary>
    internal static async Task<IResult> PollLoginAsync(BiliLogin login, BiliSession session, BiliPoller poller, string key, CancellationToken cancellationToken)
    {
        BiliQrState state = await login.PollAsync(key, cancellationToken);
        if (state == BiliQrState.Succeeded) poller.RequestPoll();
        return Results.Ok(
            new BiliQrStateView
            {
                State   = state,
                Account = state == BiliQrState.Succeeded ? session.Account : null
            });
    }

    /// <summary>当前登录账号的关注列表与各 UP 的推送设置，按昵称排列；未登录（含登录失效）时为空。</summary>
    internal static async Task<IResult> FollowsAsync(BiliSession session, BiliStore store)
    {
        if (session.State != BiliLoginState.LoggedIn || session.Account is not { } account) return Results.Ok(Array.Empty<BiliFollowView>());
        Dictionary<long, BiliSubscriptionRecord> subscriptions = (await store.SubscriptionsAsync()).ToDictionary(subscription => subscription.Uid);
        ILookup<long, long> groups = (await store.TargetsAsync()).ToLookup(target => target.Uid, target => target.GroupId);
        return Results.Ok(
            (await store.FollowsAsync(account.Uid))
            .OrderBy(follow => follow.Name, StringComparer.CurrentCulture)
            .Select(follow => new BiliFollowView
            {
                Uid           = follow.Uid,
                Name          = follow.Name,
                Face          = follow.Face,
                LastDynamicAt = follow.LastDynamicAt,
                PushDynamic   = subscriptions.GetValueOrDefault(follow.Uid)?.PushDynamic ?? false,
                PushLive      = subscriptions.GetValueOrDefault(follow.Uid)?.PushLive ?? false,
                Groups        = [.. groups[follow.Uid].Order()]
            })
            .ToArray());
    }

    /// <summary>立即刷新关注列表后返回；未登录时 409。</summary>
    internal static async Task<IResult> RefreshFollowsAsync(BiliPoller poller, BiliSession session, BiliStore store, CancellationToken cancellationToken) =>
        await poller.RefreshFollowsAsync(cancellationToken)
            ? await FollowsAsync(session, store)
            : Results.Conflict(new { error = "未登录或刷新失败，详见日志" });

    /// <summary>保存一个 UP 的推送设置；只接受当前登录账号关注的 UP。设置按 UP 保存，换号后新账号也关注时沿用。</summary>
    internal static async Task<IResult> SaveSubscriptionAsync(BiliSession session, BiliStore store, BiliPoller poller, long uid, BiliSubscriptionUpdate update, ILogger logger)
    {
        if (update.Groups.Any(group => group <= 0)) return Results.BadRequest(new { error = "群号无效" });
        if (session.State != BiliLoginState.LoggedIn || session.Account is not { } account) return Results.Conflict(new { error = "未登录" });
        if ((await store.FollowsAsync(account.Uid)).All(follow => follow.Uid != uid)) return Results.NotFound(new { error = "关注列表中没有这个 UP" });

        await store.SaveSubscriptionAsync(uid, update.PushDynamic, update.PushLive, update.Groups);
        // 新订阅首轮推送最新一条，不必等满轮询间隔。
        poller.RequestPoll();
        logger.LogInformation(
            "面板修改 B站 UP {Uid} 的推送：动态 {PushDynamic}，直播 {PushLive}，群 {Groups}",
            uid,
            update.PushDynamic,
            update.PushLive,
            string.Join("、", update.Groups));
        return Results.NoContent();
    }

    /// <summary>可选的推送群：Bot 所在的群（按群名）与已设置但 Bot 已不在的群。</summary>
    internal static async Task<IResult> GroupsAsync(IPluginInitializationContext context, BiliStore store)
    {
        IReadOnlyList<GroupInfo> joined = context.Bot.Api is { } api && await api.GetGroupListAsync() is { IsSuccess: true, Data: { } list } ? list : [];
        HashSet<long> joinedIds = [.. joined.Select(group => (long)group.GroupId)];
        IEnumerable<BiliGroupView> current = joined.OrderBy(group => group.GroupName, StringComparer.CurrentCulture)
                                                   .Select(group => new BiliGroupView
                                                   {
                                                       Id   = group.GroupId,
                                                       Name = group.GroupName
                                                   });
        IEnumerable<BiliGroupView> left = (await store.TargetsAsync()).Select(target => target.GroupId)
                                                                      .Distinct()
                                                                      .Where(group => !joinedIds.Contains(group))
                                                                      .Order()
                                                                      .Select(group => new BiliGroupView { Id = group });
        return Results.Ok(current.Concat(left).ToArray());
    }
}
