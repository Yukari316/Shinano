using Microsoft.Extensions.Logging;
using Shinano.Framework.Commands;
using Shinano.Framework.Plugins;
using Shinano.Framework.Rendering;
using Shinano.Plugin.NB2Bridge.PluginPackages;
using Shinano.Plugin.NB2Bridge.Runtime;
using Sora.Command.Attributes;
using Sora.Entities.Events;
using Sora.Entities.Message;
using MatchType = Sora.Core.Enums.MatchType;

namespace Shinano.Plugin.NB2Bridge;

/// <summary>NB2Bridge 的运维指令。</summary>
[CommandGroup(Name = "nb2-bridge")]
public sealed class Nb2Commands
{
    private const string BASE64_PREFIX = "base64://";
    private const string TITLE         = "NB2 状态";

    private readonly IPluginInitializationContext _context;
    private readonly Nb2Runner                    _runner;
    private readonly Nb2Catalog                   _catalog;
    private readonly TimeProvider                 _time;
    private readonly ILogger<Nb2Commands>         _logger;

    /// <summary>创建指令组。</summary>
    public Nb2Commands(IPluginInitializationContext context, Nb2Runner runner, Nb2Catalog catalog, TimeProvider time, ILogger<Nb2Commands> logger)
    {
        _context = context;
        _runner  = runner;
        _catalog = catalog;
        _time    = time;
        _logger  = logger;
    }

    /// <summary>
    /// 以状态报告卡片回复 NB2 进程、NoneBot 与 Python 版本（运行中时）、事件连接、ffmpeg、最近 1 分钟的自动重启次数、各 NB2 插件与最近一次错误。
    /// 仅 Super User 可用，群聊与私聊均可；绘制失败时改发文字。
    /// </summary>
    [Command(Expressions = ["NB2状态"], MatchType = MatchType.Full, SuperUserOnly = true, Description = "查看 NB2 服务状态")]
    public async ValueTask StatusAsync(MessageReceivedEvent e)
    {
        IReadOnlyList<StatusItem> items = await ItemsAsync();
        await e.ReplyAsync(Compose(items));
    }

    private async Task<IReadOnlyList<StatusItem>> ItemsAsync()
    {
        Nb2RunnerStatus status = _runner.Status();
        List<StatusItem> items =
        [
            new()
            {
                Level  = status.State == Nb2ProcessState.Running ? CardLevel.Normal : status.State is Nb2ProcessState.Failed or Nb2ProcessState.Missing ? CardLevel.Error : CardLevel.Warning,
                Name   = "进程",
                Value  = StateText(status.State),
                Detail = status is { Pid: { } pid, StartedAt: { } started } ? $"PID {pid}，已运行 {Uptime(_time.GetUtcNow() - started)}" : null
            },
            new()
            {
                Level = status.Connected ? CardLevel.Normal : CardLevel.Warning,
                Name  = "事件连接",
                Value = status.Connected ? "已连接" : "未连接"
            },
            new()
            {
                Level  = status.FfmpegError is null ? CardLevel.Normal : CardLevel.Warning,
                Name   = "ffmpeg",
                Value  = status.FfmpegDirectory is not null ? "可用" : status.FfmpegError is not null ? "不可用" : "未检查",
                // 只放版本号，路径与不可用原因见 NB2 页面与日志。
                Detail = status.FfmpegVersion
            },
            new()
            {
                Level  = status.AutoRestarts == 0 ? CardLevel.Normal : CardLevel.Warning,
                Name   = "自动重启",
                Value  = $"{status.AutoRestarts} 次",
                Detail = $"自上次就绪起，最多 {Nb2Runner.MAX_AUTO_RESTARTS} 次"
            }
        ];

        IReadOnlyList<Nb2RemotePlugin> remote = [];
        if (_runner is { IsReady: true, Control: { } control })
        {
            try
            {
                Nb2RemoteStatus process = await control.StatusAsync(CancellationToken.None);
                items.Insert(
                    1,
                    new StatusItem
                    {
                        Level  = CardLevel.Normal,
                        Name   = "NoneBot",
                        Value  = process.Nonebot,
                        Detail = $"Python {process.Python}"
                    });
                remote = await control.PluginsAsync(CancellationToken.None);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                _logger.LogWarning(exception, "读取 NB2 版本或插件状态失败");
            }
        }

        foreach (Nb2PluginEntry entry in _catalog.Entries())
        {
            Nb2RemotePlugin? live = remote.FirstOrDefault(item => item.Module == entry.Module);
            items.Add(
                new StatusItem
                {
                    Level  = live is { Loaded: false } || live?.Stats.Errors > 0 ? CardLevel.Warning : CardLevel.Normal,
                    Name   = live?.Name ?? entry.Package,
                    Value  = entry.Enabled ? "启用" : "停止",
                    Detail = live switch
                    {
                        null              => $"v{entry.Version}",
                        { Loaded: false } => $"v{entry.Version}，加载失败",
                        _                 => $"v{entry.Version}，执行 {live.Stats.Runs} 次，错误 {live.Stats.Errors} 次"
                    }
                });
        }

        if (status.LastError is { } error)
        {
            items.Add(
                new StatusItem
                {
                    Level = CardLevel.Error,
                    Name  = "最近错误",
                    Value = error
                });
        }

        return items;
    }

    private MessageBody Compose(IReadOnlyList<StatusItem> items)
    {
        try
        {
            byte[] png = _context.Images.RenderStatus(
                new StatusCard
                {
                    Title = TITLE,
                    Time  = _time.GetLocalNow(),
                    Items = items
                });
            return new MessageBody().AddImage(BASE64_PREFIX + Convert.ToBase64String(png));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "绘制 NB2 状态卡片失败，改发文字");
            return new MessageBody(
                string.Join(
                    '\n',
                    [TITLE, .. items.Select(item => item.Detail is null ? $"{item.Name}：{item.Value}" : $"{item.Name}：{item.Value}（{item.Detail}）")]));
        }
    }

    private static string StateText(Nb2ProcessState state) =>
        state switch
        {
            Nb2ProcessState.Running  => "运行中",
            Nb2ProcessState.Starting => "启动中",
            Nb2ProcessState.Stopping => "停止中",
            Nb2ProcessState.Failed   => "已停止（重启次数超限）",
            Nb2ProcessState.Missing  => "缺少运行环境",
            _                        => "已停止"
        };

    private static string Uptime(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1 ? $"{(int)elapsed.TotalHours} 小时 {elapsed.Minutes} 分钟" : $"{Math.Max(elapsed.Minutes, 0)} 分钟";
}
