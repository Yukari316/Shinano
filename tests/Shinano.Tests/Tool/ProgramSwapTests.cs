using Shinano.Tool.Deployment;

namespace Shinano.Tests.Tool;

/// <summary>升级时程序目录的替换：旧版移入 .previous，日志与监控探测记录移回新版，部署数据目录不动。</summary>
public sealed class ProgramSwapTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("shinano-swap-").FullName;

    [Fact]
    public void Replace_SwapsProgramDirectoriesAndKeepsRuntimeData()
    {
        DeploymentLayout target = DeploymentLayout.At(Path.Combine(_root, "deploy"));
        DeploymentLayout source = DeploymentLayout.At(Path.Combine(_root, "package"));
        Write(target.Root, "host/old.dll", "old");
        Write(target.Root, "host/Logs/run.log", "log");
        Write(target.Root, "monitor/data/probes.jsonl", "probe");
        Write(target.Root, "data/shinano.local.json", "{}");
        Write(target.Root, ".previous/host/older.dll", "older");
        Write(source.Root, "host/new.dll", "new");
        Write(source.Root, "monitor/Shinano.Monitor.dll", "monitor");
        Write(source.Root, "scripts/start.sh", "#!/bin/sh");

        ProgramSwap.Replace(source, target, new ToolLog(Path.Combine(_root, "tool.log"), null));

        Assert.True(File.Exists(Path.Combine(target.HostDirectory, "new.dll")));
        Assert.False(File.Exists(Path.Combine(target.HostDirectory, "old.dll")));
        Assert.Equal("log", File.ReadAllText(Path.Combine(target.HostDirectory, "Logs", "run.log")));
        Assert.Equal("probe", File.ReadAllText(Path.Combine(target.MonitorDirectory, "data", "probes.jsonl")));
        Assert.Equal("{}", File.ReadAllText(target.LocalConfigPath));
        Assert.True(File.Exists(Path.Combine(target.PreviousDirectory, "host", "old.dll")));
        Assert.False(File.Exists(Path.Combine(target.PreviousDirectory, "host", "older.dll")));
        Assert.True(File.Exists(Path.Combine(source.HostDirectory, "new.dll")));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void Write(string root, string relative, string content)
    {
        string path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
        File.WriteAllText(path, content);
    }
}
