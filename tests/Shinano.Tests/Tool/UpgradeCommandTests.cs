using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;

namespace Shinano.Tests.Tool;

/// <summary>升级的失败报告与部署配置文件改名迁移：程序目录替换成功之后的失败不能报告为已还原。</summary>
public sealed class UpgradeCommandTests : IDisposable
{
    private const string LEGACY_CONFIG_FILE_NAME = "shinano.local.json";

    private readonly string           _root   = Directory.CreateTempSubdirectory("shinano-upgrade-").FullName;
    private readonly DeploymentLayout _target;
    private readonly DeploymentLayout _source;

    public UpgradeCommandTests()
    {
        _target = DeploymentLayout.At(Path.Combine(_root, "deploy"));
        _source = DeploymentLayout.At(Path.Combine(_root, "package"));
        Write(_target.HostExecutable, "old");
        DeploymentStateStore.Write(_target, new DeploymentState());
        Write(_source.HostExecutable, "new");
    }

    [Fact]
    public async Task DirectUpgrade_ReportsNewVersionInstalled_WhenPluginSyncFailsAfterSwap()
    {
        Write(_target.ConfigPath, "{}");
        Write(Path.Combine(_target.DataDirectory, "plugin-staging", "pending.json"), "{ 损坏");

        ToolException failure = await Assert.ThrowsAsync<ToolException>(() => UpgradeCommand.RunAsync(Context()));

        Assert.Contains("已安装", failure.Message);
        Assert.DoesNotContain("已还原", failure.Message);
        Assert.Equal("new", File.ReadAllText(_target.HostExecutable));
    }

    [Fact]
    public async Task DirectUpgrade_RenamesLegacyConfigFile()
    {
        string legacy = Path.Combine(_target.DataDirectory, LEGACY_CONFIG_FILE_NAME);
        Write(legacy, """{ "Shinano": {} }""");

        Assert.Equal(0, await UpgradeCommand.RunAsync(Context()));

        Assert.False(File.Exists(legacy));
        Assert.Equal("""{ "Shinano": {} }""", File.ReadAllText(_target.ConfigPath));
    }

    [Fact]
    public async Task Upgrade_Aborts_WhenLegacyAndCurrentConfigFilesBothExist()
    {
        Write(Path.Combine(_target.DataDirectory, LEGACY_CONFIG_FILE_NAME), "{}");
        Write(_target.ConfigPath, "{}");

        ToolException failure = await Assert.ThrowsAsync<ToolException>(() => UpgradeCommand.RunAsync(Context()));

        Assert.Contains(LEGACY_CONFIG_FILE_NAME, failure.Message);
        Assert.Equal("old", File.ReadAllText(_target.HostExecutable));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ToolContext Context()
    {
        CommandArguments arguments = CommandArguments.Parse(["upgrade", "--target", _target.Root, "--" + Prompter.YES_SWITCH]);
        return new ToolContext
        {
            Arguments = arguments,
            Layout    = _source,
            Log       = new ToolLog(Path.Combine(_root, "tool.log"), null),
            Prompter  = new Prompter(arguments, interactive: false)
        };
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, content);
    }
}
