using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Rendering.Models;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;
using Shinano.Host.Rendering;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Health.Models;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Logging;
using Sora.Command.Attributes;
using Sora.Entities.Events;
using Sora.Entities.Message;

namespace Shinano.Host.Bot;

/// <summary>宿主内置的运维指令，不属于任何插件，数据库离线时仍可用。</summary>
[CommandGroup(Name = "host")]
public sealed class HostCommands
{
    private const long   BYTES_PER_MIB = 1024L * 1024;
    private const string BASE64_PREFIX = "base64://";
    private const string TITLE         = "健康检查";

    private static readonly HealthComponent[] ReportedComponents =
        [HealthComponent.Database, HealthComponent.ObjectStorage, HealthComponent.Bot];

    private readonly HostInfrastructure   _infrastructure;
    private readonly RuntimeHealth        _health;
    private readonly PluginRuntime        _plugins;
    private readonly LogFileLayout        _logLayout;
    private readonly int                  _maxLogSizeMiB;
    private readonly CardRenderer         _images;
    private readonly TimeProvider         _time;
    private readonly TimeZoneInfo         _timeZone;
    private readonly ILogger<HostCommands> _logger;

    /// <summary>创建指令组。</summary>
    public HostCommands(
        HostInfrastructure    infrastructure,
        RuntimeHealth         health,
        PluginRuntime         plugins,
        LogFileLayout         logLayout,
        int                   maxLogSizeMiB,
        CardRenderer          images,
        TimeProvider          time,
        TimeZoneInfo          timeZone,
        ILogger<HostCommands> logger)
    {
        _infrastructure = infrastructure;
        _health         = health;
        _plugins        = plugins;
        _logLayout      = logLayout;
        _maxLogSizeMiB  = maxLogSizeMiB;
        _images         = images;
        _time           = time;
        _timeZone       = timeZone;
        _logger         = logger;
    }

    /// <summary>
    /// 健康检查：强制探测数据库与 RustFS，以状态报告卡片回复数据库、RustFS、LLBot 连接、日志目录占用与已启用的插件
    /// （含初始化失败的）。仅 Super User 可用，群聊与私聊均可；绘制失败时改发文字。
    /// </summary>
    [Command(Expressions = ["健康检查"], SuperUserOnly = true, Description = "强制检查宿主依赖并回复结果")]
    public async ValueTask HealthCheckAsync(MessageReceivedEvent e)
    {
        await _infrastructure.DatabaseAvailability.CheckAsync(force: true, CancellationToken.None);
        await _infrastructure.StorageAvailability.CheckAsync(force: true, CancellationToken.None);

        IReadOnlyList<StatusItem> items = await ItemsAsync();
        await e.ReplyAsync(Compose(items));
    }

    private static CardLevel ToCardLevel(HealthLevel level) =>
        level switch
        {
            HealthLevel.Stopped  => CardLevel.Error,
            HealthLevel.Degraded => CardLevel.Warning,
            _                    => CardLevel.Normal
        };

    private async Task<IReadOnlyList<StatusItem>> ItemsAsync()
    {
        List<StatusItem> items =
        [
            .. ReportedComponents.Select(_health.Get).Select(status => new StatusItem
            {
                Level  = ToCardLevel(status.Level),
                Name   = HealthText.ComponentName(status.Component),
                Value  = HealthText.Describe(status.Level),
                Detail = status.Detail.Length > 0 ? status.Detail : null
            })
        ];

        long logBytes = new DirectoryInfo(_logLayout.Directory).EnumerateFiles().Sum(file => file.Length);
        items.Add(
            new StatusItem
            {
                Level  = CardLevel.Normal,
                Name   = "日志目录",
                Value  = $"{logBytes / BYTES_PER_MIB} MiB",
                Detail = $"上限 {_maxLogSizeMiB} MiB"
            });

        IReadOnlyList<PluginSnapshot> plugins = await _plugins.SnapshotAsync(CancellationToken.None);
        items.AddRange(
            plugins.Where(plugin => plugin.Enabled || plugin.Error is not null)
                   .Select(plugin => new StatusItem
                   {
                       Level  = plugin.Error is null ? CardLevel.Normal : CardLevel.Warning,
                       Name   = plugin.Name,
                       Value  = plugin.Error is null ? "已启用" : "失败",
                       Detail = plugin.Error is null ? $"v{plugin.Version}" : $"v{plugin.Version}，{plugin.Error}"
                   }));
        return items;
    }

    private MessageBody Compose(IReadOnlyList<StatusItem> items)
    {
        try
        {
            byte[] png = _images.RenderStatus(
                new StatusCard
                {
                    Title = TITLE,
                    Time  = TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _timeZone),
                    Items = items
                });
            return new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制健康检查卡片失败，改发文字");
            return new MessageBody(
                string.Join(
                    '\n',
                    [TITLE, .. items.Select(item => item.Detail is null ? $"{item.Name}：{item.Value}" : $"{item.Name}：{item.Value}（{item.Detail}）")]));
        }
    }
}
