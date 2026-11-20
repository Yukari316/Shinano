using Microsoft.AspNetCore.Http;
using Shinano.Framework.Plugins;
using Sora.Entities.Info;

namespace Shinano.Plugin.NB2Bridge.Scope;

/// <summary>NB2 响应范围的面板 API 处理方法，路由见 <see cref="Panel.Nb2PanelApi" />。路由中的类别为 <c>group</c> 或 <c>private</c>。</summary>
internal static class Nb2ScopeEndpoints
{
    /// <summary>群聊与私聊的响应范围。</summary>
    internal static IResult Scopes(Nb2ScopeStore store) =>
        Results.Ok(
            new Nb2ScopesView
            {
                Group   = View(store.Get(Nb2ScopeKind.Group)),
                Private = View(store.Get(Nb2ScopeKind.Private))
            });

    /// <summary>保存一类会话的响应范围，立即生效。</summary>
    internal static async Task<IResult> SaveAsync(Nb2ScopeStore store, string kind, Nb2ScopeUpdate update)
    {
        if (Parse(kind) is not { } scopeKind) return Results.NotFound(new { error = $"没有 {kind} 类会话" });
        // 请求体中显式的 null 会覆盖默认值。
        if (update.Ids is null || update.Ids.Any(id => id <= 0)) return Results.BadRequest(new { error = "号码必须是正整数" });

        await store.SaveAsync(
            new Nb2ScopeRecord
            {
                Kind      = scopeKind,
                Whitelist = update.Whitelist,
                Ids       = [.. update.Ids.Distinct().Order()]
            });
        return Results.NoContent();
    }

    /// <summary>可选的会话：Bot 当前所在的群或好友（按名称排列），之后是已在名单中但 Bot 已不在的群或已不是好友的号码。</summary>
    internal static async Task<IResult> CandidatesAsync(IBotAccessor bot, Nb2ScopeStore store, string kind, CancellationToken cancellationToken)
    {
        if (Parse(kind) is not { } scopeKind) return Results.NotFound(new { error = $"没有 {kind} 类会话" });

        IReadOnlyList<Nb2ScopeCandidateView> current = scopeKind == Nb2ScopeKind.Group
            ? await GroupsAsync(bot, cancellationToken)
            : await FriendsAsync(bot, cancellationToken);
        HashSet<long> currentIds = [.. current.Select(candidate => candidate.Id)];
        IEnumerable<Nb2ScopeCandidateView> missing = store.Get(scopeKind)
                                                          .Ids.Where(id => !currentIds.Contains(id))
                                                          .Select(id => new Nb2ScopeCandidateView { Id = id });
        return Results.Ok(current.Concat(missing).ToArray());
    }

    private static Nb2ScopeKind? Parse(string kind) =>
        kind switch
        {
            "group"   => Nb2ScopeKind.Group,
            "private" => Nb2ScopeKind.Private,
            _         => null
        };

    private static Nb2ScopeView View(Nb2ScopeRecord record) =>
        new()
        {
            Whitelist = record.Whitelist,
            Ids       = record.Ids
        };

    private static async Task<IReadOnlyList<Nb2ScopeCandidateView>> GroupsAsync(IBotAccessor bot, CancellationToken cancellationToken)
    {
        IReadOnlyList<GroupInfo> groups = bot.Api is { } api && await api.GetGroupListAsync(false, cancellationToken) is { IsSuccess: true, Data: { } list }
            ? list
            : [];
        return
        [
            .. groups.OrderBy(group => group.GroupName, StringComparer.CurrentCulture)
                     .Select(group => new Nb2ScopeCandidateView
                     {
                         Id   = group.GroupId,
                         Name = group.GroupName
                     })
        ];
    }

    private static async Task<IReadOnlyList<Nb2ScopeCandidateView>> FriendsAsync(IBotAccessor bot, CancellationToken cancellationToken)
    {
        IReadOnlyList<FriendInfo> friends = bot.Api is { } api && await api.GetFriendListAsync(false, cancellationToken) is { IsSuccess: true, Data: { } list }
            ? list
            : [];
        return
        [
            .. friends.Select(friend => new Nb2ScopeCandidateView
                      {
                          Id   = friend.UserId,
                          Name = string.IsNullOrEmpty(friend.Remark) ? friend.Nickname : friend.Remark
                      })
                      .OrderBy(candidate => candidate.Name, StringComparer.CurrentCulture)
        ];
    }
}
