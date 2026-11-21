using Shinano.Tests.Host.Restart;
using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;
using Shinano.Tool.Plugins;

namespace Shinano.Tests.Tool;

/// <summary>随包原生插件的首次装入、升级写入重启计划与 SemVer 2.0 版本比较规则。</summary>
public sealed class BundledPluginSyncTests : IDisposable
{
    private readonly PackageFixture _fixture = new();
    private readonly string         _bundled;
    private readonly ToolLog        _log;

    public BundledPluginSyncTests()
    {
        _bundled = Path.Combine(_fixture.DataDirectory, "bundled");
        Directory.CreateDirectory(_bundled);
        _log = new ToolLog(Path.Combine(_fixture.DataDirectory, "tool.log"), null);
    }

    [Fact]
    public void InstallInitial_InstallsMissingAndRecordsAll()
    {
        Bundle("sample-a", "1.0.0");
        Bundle("sample-b", "1.0.0");
        _fixture.Install("sample-b", "sample-b", "0.9.0");
        DeploymentState state = new();

        BundledPluginSync.InstallInitial(PluginPackages.ReadBundled(_bundled), _fixture.Settings.PluginRoot, state, _log);

        Assert.True(File.Exists(Path.Combine(_fixture.Settings.PluginRoot, "sample-a", "plugin.json")));
        Assert.Equal("0.9.0", PluginPackages.ScanInstalled(_fixture.Settings.PluginRoot)["sample-b"].Manifest.Version);
        Assert.Equal(["sample-a", "sample-b"], state.BundledPlugins);
    }

    [Fact]
    public void Upgrade_PlansOnlyBundledUpgrades_AndKeepsManualStagingSeparate()
    {
        Bundle("removed", "2.0.0");
        Bundle("added", "1.0.0");
        Bundle("same", "1.0.0");
        Bundle("upgraded", "1.2.0");
        Bundle("manual", "2.0.0");
        _fixture.Install("same", "same", "1.0.0");
        string upgradedDir = _fixture.Install("CustomDir", "upgraded", "1.1.0");
        _fixture.Install("manual", "manual", "1.0.0");
        _fixture.CreateStaging().StageRemoval("manual", purgeData: false);
        DeploymentState state = new() { BundledPlugins = ["removed", "same", "upgraded", "manual"] };
        DateTimeOffset now = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

        BundledUpgradeResult result = BundledPluginSync.Upgrade(
            PluginPackages.ReadBundled(_bundled),
            _fixture.Settings.PluginRoot,
            _fixture.DataDirectory,
            state,
            _log,
            now);

        IReadOnlyDictionary<string, InstalledPlugin> installed = PluginPackages.ScanInstalled(_fixture.Settings.PluginRoot);
        Assert.False(installed.ContainsKey("removed"));
        Assert.True(installed.ContainsKey("added"));
        Assert.Contains("added", state.BundledPlugins);
        Assert.Single(result.InstalledDirectories);
        Assert.Equal(["upgraded"], result.PlannedUpgrades.Select(item => item.PluginId));
        Assert.Equal(["manual"], result.SkippedManualStaging);
        Assert.Equal(1, result.OtherPendingCount);
        Assert.Equal(now, result.PlanCreatedAt);

        // 重启计划只包含本次随包插件升级，目标目录指向原插件实际目录，不包含管理员手动暂存的 manual。
        Assert.True(RestartPlanFile.Exists(_fixture.DataDirectory));
        Assert.Equal(["manual", "upgraded"], _fixture.CreateStaging().Pending().Select(item => item.Change.PluginId).Order());

        // 原始插件目录尚未被覆盖，等待宿主首次启动应用。
        Assert.Equal("1.1.0", installed["upgraded"].Manifest.Version);
        Assert.Equal(upgradedDir, installed["upgraded"].Directory);
    }

    [Fact]
    public void ValidateBeforeUpgrade_RejectsExistingRestartPlanWithGuidance()
    {
        RestartPlanFile.WritePlan(
            _fixture.DataDirectory,
            new ToolRestartPlan
            {
                CreatedAt  = DateTimeOffset.UtcNow,
                State      = ToolRestartPlanState.Pending,
                Operations = []
            });

        ToolException failure = Assert.Throws<ToolException>(() => BundledPluginSync.ValidateBeforeUpgrade(_fixture.DataDirectory));

        Assert.Contains("未完成的插件重启计划", failure.Message);
        Assert.Contains("scripts/service start", failure.Message);
    }

    [Fact]
    public void ValidateBeforeUpgrade_RejectsCorruptPendingFileWithGuidance()
    {
        string staging = BundledPluginSync.StagingDirectory(_fixture.DataDirectory);
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, "pending.json"), "{ 损坏");

        ToolException failure = Assert.Throws<ToolException>(() => BundledPluginSync.ValidateBeforeUpgrade(_fixture.DataDirectory));

        Assert.Contains("pending.json", failure.Message);
        Assert.Contains("手动删除", failure.Message);
    }

    [Theory]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0-rc.1", true)]
    [InlineData("1.0.0-beta.11", "1.0.0-beta.2", true)]
    [InlineData("1.0.0-beta", "1.0.0-alpha.1", true)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha", true)]
    [InlineData("1.0.0+build.2", "1.0.0+build.1", false)]
    [InlineData("1.1.0-alpha.1", "1.0.0", true)]
    [InlineData("1.0.0-beta", "1.0.0", false)]
    [InlineData("1.2", "1.1.9", true)]
    [InlineData("01.0.0", "1.0.0", null)]
    [InlineData("not-a-version", "1.0.0", null)]
    public void IsNewer_FollowsSemVer2(string candidate, string installed, bool? expected) =>
        Assert.Equal(expected, PluginPackages.IsNewer(candidate, installed));

    public void Dispose() => _fixture.Dispose();

    private void Bundle(string id, string version)
    {
        using Stream zip = PackageFixture.Zip(PackageFixture.Package(id, version));
        using FileStream file = File.Create(Path.Combine(_bundled, id + ".zip"));
        zip.CopyTo(file);
    }
}
