using Shinano.Host.Bot;
using Shinano.Host.Panel.Models;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Logging;
using Shinano.Infrastructure.Logging.Options;
using Sora.Core.Types;
using Sora.Entities.Info;
using Sora.Entities.Interfaces;

namespace Shinano.Host.Panel.Status.Endpoints;

/// <summary>总览与 Bot 状态。基础设施处于故障模式时仍可访问，用于查看故障原因。</summary>
internal static class StatusEndpoints
{

    internal static async Task<IResult> OverviewAsync(
        RuntimeHealth     health,
        BotAccessor       bot,
        PluginRuntime     plugins,
        LogFileLayout     logLayout,
        LoggingOptions    logging,
        IConfiguration    configuration,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<PluginSnapshot> snapshot = await plugins.SnapshotAsync(cancellationToken);
        return Results.Ok(
            new
            {
                overall = health.Overall,
                components = health.Snapshot().Select(status => new
                {
                    component = status.Component,
                    name      = HealthText.ComponentName(status.Component),
                    level     = status.Level,
                    detail    = status.Detail,
                    since     = status.Since
                }),
                bot = new
                {
                    connected = bot.Api is not null,
                    selfId    = bot.SelfId
                },
                plugins = new
                {
                    total   = snapshot.Count,
                    enabled = snapshot.Count(plugin => plugin.Enabled),
                    failed  = snapshot.Count(plugin => plugin.Error is not null)
                },
                logs = new
                {
                    usedBytes = new DirectoryInfo(logLayout.Directory).EnumerateFiles().Sum(file => file.Length),
                    limitMiB  = logging.MaxTotalSizeMiB
                },
                statusPage = StatusPageLink.From(configuration)
            });
    }

    internal static async Task<IResult> BotAsync(BotAccessor bot, CancellationToken cancellationToken)
    {
        IBotApi? api = bot.Api;
        if (api is null)
            return Results.Ok(
                new
                {
                    connected = false,
                    selfId    = bot.SelfId
                });

        ApiResult<BotIdentity> self = await api.GetSelfInfoAsync(cancellationToken);
        ApiResult<ImplInfo> impl = await api.GetImplInfoAsync(cancellationToken);
        ApiResult<IReadOnlyList<GroupInfo>> groups = await api.GetGroupListAsync(false, cancellationToken);
        ApiResult<IReadOnlyList<FriendInfo>> friends = await api.GetFriendListAsync(false, cancellationToken);
        return Results.Ok(
            new
            {
                connected   = true,
                selfId      = bot.SelfId,
                nickname    = self is { IsSuccess: true, Data: { } identity } ? identity.Nickname : null,
                impl        = impl is { IsSuccess: true, Data: { } info } ? $"{info.ImplName} {info.ImplVersion}" : null,
                groupCount  = groups is { IsSuccess: true, Data: { } groupList } ? groupList.Count : (int?)null,
                friendCount = friends is { IsSuccess: true, Data: { } friendList } ? friendList.Count : (int?)null
            });
    }
}
