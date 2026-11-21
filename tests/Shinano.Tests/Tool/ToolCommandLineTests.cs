using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;
using Shinano.Tool.Run;
using Shinano.Tool.Services;
using Shinano.Tool.Setup;

namespace Shinano.Tests.Tool;

/// <summary>参数解析、Windows 命令行引号、重新配置时的服务重装判断与守护者决定。</summary>
public sealed class ToolCommandLineTests
{
    [Fact]
    public void Parse_ReadsPositionalsOptionsAndSwitches()
    {
        CommandArguments arguments = CommandArguments.Parse(["service", "install", "--db-port", "5433", "--timezone=UTC", "--accept-defaults"]);

        Assert.Equal(["service", "install"], arguments.Positionals);
        Assert.Equal("5433", arguments.Value("db-port"));
        Assert.Equal("UTC", arguments.Value("timezone"));
        Assert.True(arguments.Switch("accept-defaults"));
        Assert.Throws<ToolException>(() => arguments.RequireKnown(["db-port", "accept-defaults"]));
        Assert.Throws<ToolException>(() => CommandArguments.Parse(["init", "--db-port"]));
    }

    // 重跑 init 时运行用户的变化曾不触发重装，部署状态与服务实际的运行用户不再一致。
    [Fact]
    public void InstalledServices_RequireReinstallWhenModeNameOrUserChanges()
    {
        DeploymentState state = new()
        {
            RunMode           = RunMode.Service,
            ServicesInstalled = true,
            ServiceUser       = "shinano"
        };
        InstalledServices installed = InstalledServices.Of(state);

        Assert.True(installed.Matches(state));
        state.ServiceUser = null;
        Assert.False(installed.Matches(state));
        state.ServiceUser = "shinano";
        state.HostServiceName = "Other";
        Assert.False(installed.Matches(state));
        state.HostServiceName = DeploymentState.DEFAULT_HOST_SERVICE_NAME;
        state.RunMode = RunMode.Direct;
        Assert.False(installed.Matches(state));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(@"C:\Program Files\x.exe", "\"C:\\Program Files\\x.exe\"")]
    [InlineData("\"C:\\a b\\x.exe\"", "\"\\\"C:\\a b\\x.exe\\\"\"")]
    [InlineData(@"C:\dir with space\", "\"C:\\dir with space\\\\\"")]
    [InlineData("", "\"\"")]
    public void QuoteWindows_FollowsCommandLineRules(string argument, string expected) =>
        Assert.Equal(expected, CommandRunner.QuoteWindows(argument));

    [Fact]
    public void SupervisorPolicy_RestartsOnlyOnRestartExitCode()
    {
        Assert.Equal(SupervisorDecision.RestartAndRetryIfNotReady, SupervisorPolicy.InitialWatch(planPending: true));
        Assert.Equal(SupervisorDecision.RestartAndReport, SupervisorPolicy.InitialWatch(planPending: false));
        Assert.Equal(SupervisorDecision.Stop, SupervisorPolicy.AfterExit(HostContract.RESTART_EXIT_CODE, true, false, true, 0));
        Assert.Equal(SupervisorDecision.RestartAndRetryIfNotReady, SupervisorPolicy.AfterExit(HostContract.RESTART_EXIT_CODE, false, false, true, 0));
        Assert.Equal(SupervisorDecision.RestartAndReport, SupervisorPolicy.AfterExit(HostContract.RESTART_EXIT_CODE, false, false, false, 0));
        Assert.Equal(SupervisorDecision.RestartAndReport, SupervisorPolicy.AfterExit(-1, false, true, true, 1));
        Assert.Equal(SupervisorDecision.Exit, SupervisorPolicy.AfterExit(1, false, false, false, 0));
        Assert.Equal(
            SupervisorDecision.GiveUp,
            SupervisorPolicy.AfterExit(HostContract.RESTART_EXIT_CODE, false, false, true, SupervisorPolicy.MAX_RESTARTS_WITHOUT_READY));
    }
}
