using Shinano.Tests.Host.Restart;
using Shinano.Tool.Deployment;
using Shinano.Tool.Plugins;

namespace Shinano.Tests.Tool;

/// <summary>随包原生插件的首次装入与升级规则：已删除的不装回，新增的直接装入，已有暂存时不覆盖。</summary>
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
    public void Upgrade_SkipsDeletedInstallsNewAndKeepsManualStaging()
    {
        Bundle("removed", "2.0.0");
        Bundle("added", "1.0.0");
        Bundle("same", "1.0.0");
        Bundle("manual", "2.0.0");
        _fixture.Install("same", "same", "1.0.0");
        _fixture.Install("manual", "manual", "1.0.0");
        _fixture.CreateStaging().StageRemoval("manual", purgeData: false);
        DeploymentState state = new() { BundledPlugins = ["removed", "same", "manual"] };

        BundledPluginSync.Upgrade(
            PluginPackages.ReadBundled(_bundled),
            _fixture.Settings.PluginRoot,
            _fixture.DataDirectory,
            state,
            _log,
            DateTimeOffset.UtcNow);

        IReadOnlyDictionary<string, InstalledPlugin> installed = PluginPackages.ScanInstalled(_fixture.Settings.PluginRoot);
        Assert.False(installed.ContainsKey("removed"));
        Assert.True(installed.ContainsKey("added"));
        Assert.Contains("added", state.BundledPlugins);
        // 同版本不暂存；手动暂存的删除保持不变。
        Assert.Equal(["manual"], _fixture.CreateStaging().Pending().Select(item => item.Change.PluginId));
    }

    [Fact]
    public void IsNewer_ComparesNumericVersions()
    {
        Assert.True(PluginPackages.IsNewer("1.10.0", "1.9.0"));
        Assert.False(PluginPackages.IsNewer("1.0.0", "1.0.0"));
        Assert.Null(PluginPackages.IsNewer("1.0.0-beta", "1.0.0"));
    }

    public void Dispose() => _fixture.Dispose();

    private void Bundle(string id, string version)
    {
        using Stream zip = PackageFixture.Zip(PackageFixture.Package(id, version));
        using FileStream file = File.Create(Path.Combine(_bundled, id + ".zip"));
        zip.CopyTo(file);
    }
}
