using System.Text;
using Shinano.Framework.Messaging;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Health.Models;
using Shinano.Infrastructure.Hosting;
using Shinano.Infrastructure.Logging;
using Sora.Command.Attributes;
using Sora.Entities.Events;

namespace Shinano.Host.Bot;

/// <summary>宿主内置的运维指令，不属于任何插件，数据库离线时仍可用。</summary>
[CommandGroup(Name = "host")]
public sealed class HostCommands
{
    private const long BYTES_PER_MIB = 1024L * 1024;

    private readonly HostInfrastructure _infrastructure;
    private readonly RuntimeHealth      _health;
    private readonly PluginRuntime      _plugins;
    private readonly LogFileLayout      _logLayout;
    private readonly int                _maxLogSizeMiB;

    /// <summary>创建指令组。</summary>
    public HostCommands(
        HostInfrastructure infrastructure,
        RuntimeHealth      health,
        PluginRuntime      plugins,
        LogFileLayout      logLayout,
        int                maxLogSizeMiB)
    {
        _infrastructure = infrastructure;
        _health         = health;
        _plugins        = plugins;
        _logLayout      = logLayout;
        _maxLogSizeMiB  = maxLogSizeMiB;
    }

    /// <summary>
    /// 健康检查：强制探测数据库与 RustFS，回复各组成部分的状态与日志目录占用。仅 Super User 可用，群聊与私聊均可。
    /// </summary>
    [Command(Expressions = ["健康检查"], SuperUserOnly = true, Description = "强制检查宿主依赖并回复结果")]
    public async ValueTask HealthCheckAsync(MessageReceivedEvent e)
    {
        await _infrastructure.DatabaseAvailability.CheckAsync(force: true, CancellationToken.None);
        await _infrastructure.StorageAvailability.CheckAsync(force: true, CancellationToken.None);

        StringBuilder reply = new();
        reply.AppendLine($"健康检查：{HealthText.Describe(_health.Overall)}");
        foreach (HealthStatus status in _health.Snapshot())
        {
            reply.Append($"{HealthText.ComponentName(status.Component)}：{HealthText.Describe(status.Level)}");
            if (status.Detail.Length > 0) reply.Append($"（{status.Detail}）");
            reply.AppendLine();
        }

        IReadOnlyList<PluginSnapshot> plugins = await _plugins.SnapshotAsync(CancellationToken.None);
        reply.AppendLine($"插件：已启用 {plugins.Count(plugin => plugin.Enabled)} / {plugins.Count}");
        long logBytes = new DirectoryInfo(_logLayout.Directory).EnumerateFiles().Sum(file => file.Length);
        reply.Append($"日志目录：{logBytes / BYTES_PER_MIB} MiB / {_maxLogSizeMiB} MiB");
        await e.ReplyAsync(reply.ToString());
    }
}
