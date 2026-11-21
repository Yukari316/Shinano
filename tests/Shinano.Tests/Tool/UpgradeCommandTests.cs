using Shinano.Tests.Host.Restart;
using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;
using Shinano.Tool.Plugins;
using Shinano.Tool.Services;

namespace Shinano.Tests.Tool;

/// <summary>升级的前置拦截、失败报告、版本方向校验、服务模式核验、结果核对与部署配置文件改名迁移。</summary>
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
    public async Task DirectUpgrade_AbortsBeforeSwap_WhenPendingFileIsCorrupt()
    {
        Write(_target.ConfigPath, "{}");
        Write(Path.Combine(_target.DataDirectory, "plugin-staging", "pending.json"), "{ 损坏");

        ToolException failure = await Assert.ThrowsAsync<ToolException>(() => UpgradeCommand.RunAsync(Context()));

        Assert.Contains("pending.json", failure.Message);
        Assert.Equal("old", File.ReadAllText(_target.HostExecutable));
    }

    [Fact]
    public async Task DirectUpgrade_AbortsBeforeSwap_WhenRestartPlanExists()
    {
        Write(_target.ConfigPath, "{}");
        RestartPlanFile.WritePlan(
            _target.DataDirectory,
            new ToolRestartPlan
            {
                CreatedAt  = DateTimeOffset.UtcNow,
                State      = ToolRestartPlanState.Pending,
                Operations = []
            });

        ToolException failure = await Assert.ThrowsAsync<ToolException>(() => UpgradeCommand.RunAsync(Context()));

        Assert.Contains("未完成的插件重启计划", failure.Message);
        Assert.Equal("old", File.ReadAllText(_target.HostExecutable));
    }

    // 替换程序目录成功后，如果后续随包插件同步阶段发生 IO 失败，必须报告新版本已安装而非已还原。
    [Fact]
    public async Task DirectUpgrade_ReportsNewVersionInstalled_WhenPostSwapStepFails()
    {
        Write(_target.ConfigPath, "{}");
        string bundledDir = _source.BundledPluginsDirectory;
        Directory.CreateDirectory(bundledDir);
        using (Stream zip = PackageFixture.Zip(PackageFixture.Package("sample-a", "1.1.0")))
        using (FileStream file = File.Create(Path.Combine(bundledDir, "sample-a.zip")))
            zip.CopyTo(file);

        // 预装旧版插件，并将 data/restart 占成普通文件：前置校验可通过，替换程序目录后写入 restart/plan.json 时抛 IOException。
        string installed = Path.Combine(_target.DataDirectory, "plugins", "sample-a");
        Directory.CreateDirectory(installed);
        Write(Path.Combine(installed, "plugin.json"), """{"id":"sample-a","name":"Sample","version":"1.0.0","entryAssembly":"Sample.dll"}""");
        Write(Path.Combine(_target.DataDirectory, "restart"), "file-blocking-directory");

        ToolException failure = await Assert.ThrowsAsync<ToolException>(() => UpgradeCommand.RunAsync(Context()));

        Assert.Contains("已安装", failure.Message);
        Assert.DoesNotContain("已还原", failure.Message);
        Assert.Equal("new", File.ReadAllText(_target.HostExecutable));
    }

    [Fact]
    public async Task DirectUpgrade_AcceptsAcceptDefaultsSwitch()
    {
        Write(_target.ConfigPath, "{}");

        CommandArguments arguments = CommandArguments.Parse(["upgrade", "--target", _target.Root, "--" + ToolContext.ACCEPT_DEFAULTS_SWITCH]);
        arguments.RequireKnown(UpgradeCommand.Options);

        Assert.Equal(0, await UpgradeCommand.RunAsync(Context(arguments)));
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

    [Fact]
    public void CheckVersionDirection_RejectsDowngradeAndAllowsSameVersionNonInteractive()
    {
        ToolLog log = new(Path.Combine(_root, "tool.log"), null);
        ToolContext context = Context();

        ToolException downgrade = Assert.Throws<ToolException>(
            () => UpgradeCommand.CheckVersionDirection("1.2.0", "1.1.0", _target, context, service: false, log));
        Assert.Contains("不支持从 1.2.0 降级到 1.1.0", downgrade.Message);

        // 非交互模式允许同版本覆盖重装。
        UpgradeCommand.CheckVersionDirection("1.1.0", "1.1.0", _target, context, service: false, log);
        UpgradeCommand.CheckVersionDirection("1.0.0", "1.1.0", _target, context, service: false, log);
    }

    [Fact]
    public void VerifyServiceAndResolveOwner_CorrectsInstalledFlagAndPrefersSystemdUser()
    {
        DeploymentState state = new()
        {
            RunMode           = RunMode.Service,
            ServicesInstalled = false,
            ServiceUser       = null
        };
        ServiceSetup setup = ServiceManagers.Setup(_target, state);
        ToolLog log = new(Path.Combine(_root, "tool.log"), null);
        FakeServiceManager manager = new(
            new Dictionary<string, ServiceStatus>
            {
                [setup.Host.Name] = new ServiceStatus
                {
                    Exists     = true,
                    Running    = true,
                    Executable = setup.Host.Executable,
                    User       = "deploy-user"
                },
                [setup.Monitor.Name] = new ServiceStatus
                {
                    Exists     = true,
                    Running    = true,
                    Executable = setup.Monitor.Executable,
                    User       = "deploy-user"
                }
            });

        string? owner = UpgradeCommand.VerifyServiceAndResolveOwner(manager, setup, state, isLinux: true, log);

        Assert.True(state.ServicesInstalled);
        Assert.Equal("deploy-user", owner);
    }

    [Fact]
    public void VerifyServiceAndResolveOwner_RejectsMissingServiceInsteadOfFallingBack()
    {
        DeploymentState state = new()
        {
            RunMode           = RunMode.Service,
            ServicesInstalled = false
        };
        ServiceSetup setup = ServiceManagers.Setup(_target, state);
        ToolLog log = new(Path.Combine(_root, "tool.log"), null);
        FakeServiceManager manager = new(new Dictionary<string, ServiceStatus>());

        ToolException failure = Assert.Throws<ToolException>(
            () => UpgradeCommand.VerifyServiceAndResolveOwner(manager, setup, state, isLinux: true, log));

        Assert.Contains("服务管理器中不存在", failure.Message);
    }

    [Fact]
    public void ReportRestartOutcome_DetectsSucceededRolledBackAndUnverified()
    {
        DateTimeOffset createdAt = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        ToolLog log = new(Path.Combine(_root, "tool.log"), null);

        WriteResult(ToolRestartOutcome.Succeeded, null, createdAt.AddSeconds(5));
        Assert.True(UpgradeCommand.ReportRestartOutcome(_target.DataDirectory, createdAt, log));

        WriteResult(ToolRestartOutcome.RolledBack, "入口程序集缺失", createdAt.AddSeconds(10));
        Assert.False(UpgradeCommand.ReportRestartOutcome(_target.DataDirectory, createdAt, log));

        WriteResult(ToolRestartOutcome.Unverified, "故障模式", createdAt.AddSeconds(15));
        Assert.False(UpgradeCommand.ReportRestartOutcome(_target.DataDirectory, createdAt, log));

        // 历史旧结果不能误判为本次升级成功。
        WriteResult(ToolRestartOutcome.Succeeded, null, createdAt.AddMinutes(-5));
        Assert.False(UpgradeCommand.ReportRestartOutcome(_target.DataDirectory, createdAt, log));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private ToolContext Context(CommandArguments? arguments = null)
    {
        CommandArguments parsed = arguments ?? CommandArguments.Parse(["upgrade", "--target", _target.Root, "--" + Prompter.YES_SWITCH]);
        return new ToolContext
        {
            Arguments = parsed,
            Layout    = _source,
            Log       = new ToolLog(Path.Combine(_root, "tool.log"), null),
            Prompter  = new Prompter(parsed, interactive: false)
        };
    }

    private void WriteResult(ToolRestartOutcome outcome, string? reason, DateTimeOffset finishedAt)
    {
        string path = RestartPlanFile.ResultPath(_target.DataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _target.DataDirectory);
        string json = $$"""
                        {
                          "outcome": "{{outcome}}",
                          "reason": {{(reason is null ? "null" : $"\"{reason}\"")}},
                          "finishedAt": "{{finishedAt:O}}",
                          "changes": [
                            {
                              "kind": "Upgrade",
                              "pluginId": "sample-a",
                              "name": "Sample",
                              "fromVersion": "1.0.0",
                              "toVersion": "1.1.0",
                              "purgeData": false
                            }
                          ]
                        }
                        """;
        File.WriteAllText(path, json);
    }

    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, content);
    }

    private sealed class FakeServiceManager : IServiceManager
    {
        private readonly IReadOnlyDictionary<string, ServiceStatus> _statuses;

        public FakeServiceManager(IReadOnlyDictionary<string, ServiceStatus> statuses)
        {
            _statuses = statuses;
        }

        public void Prepare(ServiceSetup setup)
        {
        }

        public IReadOnlyList<SystemCommand> InstallCommands(ServiceSetup setup) => [];

        public IReadOnlyList<SystemCommand> UninstallCommands(ServiceSetup setup, bool deleteUser, string? owner) => [];

        public IReadOnlyList<SystemCommand> StartCommands(ServiceSetup setup) => [];

        public IReadOnlyList<SystemCommand> StopCommands(ServiceSetup setup) => [];

        public ServiceStatus Query(string name) =>
            _statuses.TryGetValue(name, out ServiceStatus? status)
                ? status
                : new ServiceStatus
                {
                    Exists  = false,
                    Running = false
                };

        public Task<bool> WaitAsync(string name, bool running, TimeSpan timeout) => Task.FromResult(true);
    }
}
