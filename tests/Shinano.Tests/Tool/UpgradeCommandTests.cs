using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;

namespace Shinano.Tests.Tool;

/// <summary>升级的失败报告：程序目录替换成功之后的失败不能报告为已还原。</summary>
public sealed class UpgradeCommandTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("shinano-upgrade-").FullName;

    [Fact]
    public async Task DirectUpgrade_ReportsNewVersionInstalled_WhenPluginSyncFailsAfterSwap()
    {
        DeploymentLayout target = DeploymentLayout.At(Path.Combine(_root, "deploy"));
        DeploymentLayout source = DeploymentLayout.At(Path.Combine(_root, "package"));
        Write(target.HostExecutable, "old");
        Write(target.LocalConfigPath, "{}");
        Write(Path.Combine(target.DataDirectory, "plugin-staging", "pending.json"), "{ 损坏");
        DeploymentStateStore.Write(target, new DeploymentState());
        Write(source.HostExecutable, "new");
        CommandArguments arguments = CommandArguments.Parse(["upgrade", "--target", target.Root, "--" + Prompter.YES_SWITCH]);
        ToolContext context = new()
        {
            Arguments = arguments,
            Layout    = source,
            Log       = new ToolLog(Path.Combine(_root, "tool.log"), null),
            Prompter  = new Prompter(arguments, interactive: false)
        };

        ToolException failure = await Assert.ThrowsAsync<ToolException>(() => UpgradeCommand.RunAsync(context));

        Assert.Contains("已安装", failure.Message);
        Assert.DoesNotContain("已还原", failure.Message);
        Assert.Equal("new", File.ReadAllText(target.HostExecutable));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, content);
    }
}
