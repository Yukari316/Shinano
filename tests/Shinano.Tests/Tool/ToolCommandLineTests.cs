using Shinano.Tool.Cli;
using Shinano.Tool.Run;
using Shinano.Tool.Run.Models;
using Shinano.Tool.Services;

namespace Shinano.Tests.Tool;

/// <summary>参数解析、Windows 命令行引号与守护者决定。</summary>
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
