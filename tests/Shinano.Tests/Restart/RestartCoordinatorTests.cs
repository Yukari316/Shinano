using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Shinano.Host.Bot;
using Shinano.Host.Bot.Models;
using Shinano.Host.Configuration;
using Shinano.Host.Plugins;
using Shinano.Host.Plugins.Models;
using Shinano.Host.Restart;
using Shinano.Host.Restart.Models;
using Shinano.Infrastructure.Health;
using Shinano.Infrastructure.Hosting.Models;
using Shinano.Tests.Rendering;

namespace Shinano.Tests.Restart;

/// <summary>
/// 重启的完整周期：重启写入计划，"新进程"启动时应用，加载验证决定保留或在下一次启动还原。
/// 每个协调器实例代表一个进程。
/// </summary>
public sealed class RestartCoordinatorTests : IDisposable
{
    private readonly PackageFixture   _fixture = new();
    private readonly PluginDataPurger _purger;

    public RestartCoordinatorTests() => _purger = _fixture.CreatePurger();

    public void Dispose()
    {
        // 协调器在验证失败或服务管理器模式重启时设置进程退出码，测试结束后复原。
        Environment.ExitCode = 0;
        _fixture.Dispose();
    }

    [Fact]
    public async Task Upgrade_ThatLoads_IsKept()
    {
        string installed = _fixture.Install("SampleA", "sample-a", "1.0.0");
        await StageAsync("sample-a", "1.0.1");
        RequestRestart(keepBackup: false);

        RestartCoordinator next = Process(keepBackup: false);
        next.PrepareStartup();
        Assert.Equal("1.0.1", Version(installed));

        Assert.True(next.OnPluginsLoaded(new Dictionary<string, string?> { ["sample-a"] = null }));
        Assert.Equal(StartupPhase.Ready, next.Phase);
        Assert.Equal(RestartOutcome.Succeeded, next.LastResult?.Outcome);
        Assert.Empty(_fixture.CreateStaging().Pending());
        Assert.False(Directory.Exists(BackupRoot));

        // 之后的普通启动不再改动插件目录。
        Process(keepBackup: false).PrepareStartup();
        Assert.Equal("1.0.1", Version(installed));
    }

    [Fact]
    public async Task Upgrade_ThatFailsToLoad_IsRolledBackOnNextStart()
    {
        string installed = _fixture.Install("SampleA", "sample-a", "1.0.0");
        await StageAsync("sample-a", "1.0.1");
        await StageAsync("sample-b", "1.0.0");
        RequestRestart(keepBackup: true);

        FakeLifetime lifetime = new();
        RestartCoordinator next = Process(keepBackup: true, lifetime);
        next.PrepareStartup();
        Assert.True(Directory.Exists(Path.Combine(_fixture.Settings.PluginRoot, "sample-b")));

        Assert.False(next.OnPluginsLoaded(new Dictionary<string, string?> { ["sample-a"] = "入口程序集缺失", ["sample-b"] = null }));
        Assert.True(lifetime.StopRequested);
        Assert.Equal(RestartOptions.SERVICE_RESTART_EXIT_CODE, Environment.ExitCode);

        RestartCoordinator restored = Process(keepBackup: true);
        restored.PrepareStartup();
        Assert.Equal("1.0.0", Version(installed));
        Assert.False(Directory.Exists(Path.Combine(_fixture.Settings.PluginRoot, "sample-b")));
        Assert.False(Directory.Exists(BackupRoot));
        RestartResult result = Assert.IsType<RestartResult>(restored.LastResult);
        Assert.Equal(RestartOutcome.RolledBack, result.Outcome);
        Assert.Contains("入口程序集缺失", result.Reason);
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public async Task ProcessExitingBeforeVerification_IsRolledBackOnNextStart()
    {
        string installed = _fixture.Install("SampleA", "sample-a", "1.0.0");
        _fixture.CreateStaging().StageRemoval("sample-a", purgeData: false);
        await StageAsync("sample-c", "1.0.0");
        RequestRestart(keepBackup: true);

        Process(keepBackup: true).PrepareStartup();
        Assert.False(Directory.Exists(installed));

        // 新进程未完成验证就退出（崩溃或被辅助程序结束），下一次启动还原。
        RestartCoordinator restored = Process(keepBackup: true);
        restored.PrepareStartup();
        Assert.Equal("1.0.0", Version(installed));
        Assert.False(Directory.Exists(Path.Combine(_fixture.Settings.PluginRoot, "sample-c")));
        Assert.Equal(RestartOutcome.RolledBack, restored.LastResult?.Outcome);
    }

    [Fact]
    public async Task FaultMode_LeavesChangesAppliedAsUnverified()
    {
        string installed = _fixture.Install("SampleA", "sample-a", "1.0.0");
        await StageAsync("sample-a", "1.0.1");
        RequestRestart(keepBackup: true);

        RestartCoordinator next = Process(keepBackup: true);
        next.PrepareStartup();
        next.OnFaultMode();

        Assert.Equal(StartupPhase.FaultMode, next.Phase);
        Assert.Equal(RestartOutcome.Unverified, next.LastResult?.Outcome);
        Process(keepBackup: true).PrepareStartup();
        Assert.Equal("1.0.1", Version(installed));
        Assert.True(Directory.Exists(BackupRoot));
    }

    [Fact]
    public void RemovalWithPurge_SchedulesPurgeFromManifestOnlyAfterVerification()
    {
        string installed = _fixture.Install("SampleA", "sample-a", "1.0.0");
        _fixture.DeclareTables(installed, "sample-a", "1.0.0", ["sample_a_note"], ["sample_a_entry"], "samplea");
        Assert.True(_fixture.CreateStaging().StageRemoval("sample-a", purgeData: true)?.Change.PurgeData);
        RequestRestart(keepBackup: false);

        RestartCoordinator next = Process(keepBackup: false);
        next.PrepareStartup();
        Assert.False(Directory.Exists(installed));
        Assert.Empty(_purger.Visible());

        Assert.True(next.OnPluginsLoaded(new Dictionary<string, string?>()));
        PluginPurge purge = Assert.Single(_purger.Visible());
        Assert.Equal("sample-a", purge.PluginId);
        Assert.Equal(PluginPurgeState.Waiting, purge.State);
        Assert.Equal(["sample_a_note"], purge.Scope.Tables.Global);
        Assert.Equal(["sample_a_entry"], purge.Scope.Tables.Account);
        Assert.Equal(["samplea"], purge.Scope.BucketPurposes);
        Assert.True(Assert.Single(next.LastResult?.Changes ?? []).PurgeData);
    }

    [Fact]
    public void RemovalWithPurge_RolledBack_DoesNotPurge()
    {
        string installed = _fixture.Install("SampleA", "sample-a", "1.0.0");
        _fixture.CreateStaging().StageRemoval("sample-a", purgeData: true);
        RequestRestart(keepBackup: true);

        Process(keepBackup: true).PrepareStartup();
        // 新进程未完成验证就退出，下一次启动还原。
        RestartCoordinator restored = Process(keepBackup: true);
        restored.PrepareStartup();

        Assert.True(Directory.Exists(installed));
        Assert.Equal(RestartOutcome.RolledBack, restored.LastResult?.Outcome);
        Assert.Empty(_purger.Visible());
    }

    [Fact]
    public async Task NormalStart_DoesNotApplyPendingChanges()
    {
        string installed = _fixture.Install("SampleA", "sample-a", "1.0.0");
        await StageAsync("sample-a", "1.0.1");

        Process(keepBackup: true).PrepareStartup();

        Assert.Equal("1.0.0", Version(installed));
        Assert.Single(_fixture.CreateStaging().Pending());
    }

    private string BackupRoot => Path.Combine(_fixture.Settings.PluginRoot, ".backup");

    private async Task StageAsync(string id, string version)
    {
        await using Stream zip = PackageFixture.Zip(PackageFixture.Package(id, version));
        await _fixture.CreateStaging().StageAsync(zip, TestContext.Current.CancellationToken);
    }

    private void RequestRestart(bool keepBackup)
    {
        Assert.Null(Process(keepBackup).RequestRestart());
        Assert.Equal(RestartOptions.SERVICE_RESTART_EXIT_CODE, Environment.ExitCode);
        Environment.ExitCode = 0;
    }

    private RestartCoordinator Process(bool keepBackup, FakeLifetime? lifetime = null)
    {
        DeploymentPaths paths = new()
        {
            BaseDirectory = _fixture.DataDirectory,
            DataDirectory = _fixture.DataDirectory
        };
        RuntimeHealth health = new(TimeProvider.System);
        return new RestartCoordinator(
            paths,
            new RestartOptions { KeepBackup = keepBackup },
            _fixture.Settings,
            _fixture.CreateStaging(),
            _purger,
            lifetime ?? new FakeLifetime(),
            null,
            new SuperUserNotifier(new BotAccessor(new LlBotOptions(), new DeploymentOptions()), new DeploymentOptions(), TestRendering.Images, TimeProvider.System, NullLogger<SuperUserNotifier>.Instance),
            health,
            TimeProvider.System,
            NullLogger<RestartCoordinator>.Instance);
    }

    private static string Version(string directory) => File.ReadAllText(Path.Combine(directory, "Sample.dll"));

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public bool StopRequested { get; private set; }

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => StopRequested = true;
    }
}
