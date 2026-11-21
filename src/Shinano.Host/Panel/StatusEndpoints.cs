using System.Net;
using System.Net.Sockets;
using Shinano.Host.Bot;
using Shinano.Host.Plugins;
using Shinano.Host.Web;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Logging;
using Sora.Core.Types;
using Sora.Entities.Info;
using Sora.Entities.Interfaces;

namespace Shinano.Host.Panel;

/// <summary>总览与 Bot 状态。基础设施处于故障模式时仍可访问，用于查看故障原因。</summary>
internal static class StatusEndpoints
{

    internal static async Task<IResult> OverviewAsync(
        RuntimeHealth     health,
        BotAccessor       bot,
        PluginRuntime     plugins,
        LogFileLayout     logLayout,
        LoggingOptions    logging,
        DeploymentOptions deployment,
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
                    enabled = snapshot.Count(plugin => plugin.Status == PluginStatus.Active),
                    failed  = snapshot.Count(plugin => plugin.Status is PluginStatus.InitFailed or PluginStatus.LoadFailed)
                },
                logs = new
                {
                    usedBytes = new DirectoryInfo(logLayout.Directory).EnumerateFiles().Sum(file => file.Length),
                    limitMiB  = logging.MaxTotalSizeMiB
                },
                statusPage = StatusPageLink.From(configuration),
                // 统计按部署时区分日，面板的"今天"与日期列表也按它计算；故障模式下同样可用。
                timeZone = deployment.TimeZone
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
                friendCount = friends is { IsSuccess: true, Data: { } friendList } ? friendList.Count : (int?)null,
                groups      = groups is { IsSuccess: true, Data: { } groupItems }
                    ? groupItems.OrderBy(group => group.GroupName, StringComparer.CurrentCulture)
                                .Select(group => new
                                {
                                    groupId        = (long)group.GroupId,
                                    name           = group.GroupName,
                                    memberCount    = group.MemberCount,
                                    maxMemberCount = group.MaxMemberCount
                                })
                    : null
            });
    }
}

/// <summary>
/// 面板侧边栏的服务状态页链接，按 <c>Shinano:Monitor</c> 配置构造，默认值见 <see cref="MonitorOptions" />；
/// 配置了证书时使用 HTTPS；地址无效或为全部网卡地址时监控回退到回环地址。
/// </summary>
public sealed class StatusPageLink
{
    /// <summary>状态页地址。</summary>
    public required string Url { get; init; }

    /// <summary>状态页是否只监听回环地址，即只能在部署机本机打开。</summary>
    public required bool LoopbackOnly { get; init; }

    /// <summary>按配置构造链接。</summary>
    public static StatusPageLink From(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection(MonitorOptions.SECTION_NAME);
        // 地址无效时状态页回退到回环地址监听，链接同样指向回环地址。
        IPAddress address = section["ListenAddress"] is { Length: > 0 } value
            ? WebEndpointResolver.ParseListenAddress(value, out _) ?? IPAddress.Loopback
            : IPAddress.Loopback;
        int port = section.GetValue("Port", MonitorOptions.DEFAULT_PORT) is var configured and >= 1 and <= 65535
            ? configured
            : MonitorOptions.DEFAULT_PORT;
        string scheme = string.IsNullOrWhiteSpace(section["CertificatePath"]) ? "http" : "https";
        string host = address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();
        return new StatusPageLink
        {
            Url          = $"{scheme}://{host}:{port}/",
            LoopbackOnly = IPAddress.IsLoopback(address)
        };
    }
}
