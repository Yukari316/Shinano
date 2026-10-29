using System.ComponentModel;
using System.Diagnostics;
using Shinano.Tool.Cli;

namespace Shinano.Tool.Services;

/// <summary>
/// 修改系统状态所需的权限。没有权限时，以管理员身份重新运行本工具的同一命令：Windows 经 UAC，Linux 经 <c>sudo</c>。
/// 只有交互运行时才提权，非交互运行不弹 UAC、不调用会提示密码的 <c>sudo</c>。
/// </summary>
public static class Privilege
{
    private const int    ERROR_CANCELLED = 1223;
    private const string RUN_AS_VERB     = "runas";
    private const string SUDO            = "sudo";

    private static readonly string[] SudoLocations = ["/usr/bin/sudo", "/bin/sudo", "/usr/local/bin/sudo"];

    /// <summary>本进程是否已有管理员（Windows 已提权，Linux 为 root）权限。</summary>
    public static bool IsPrivileged => Environment.IsPrivilegedProcess;

    /// <summary>
    /// 以管理员身份运行本工具，参数为 <paramref name="arguments" /> 加上子进程标记。
    /// </summary>
    /// <returns>子进程的退出码；无法提权（非交互、拒绝 UAC、没有 sudo）时为 null。</returns>
    public static int? RunElevated(ToolContext context, IReadOnlyList<string> arguments)
    {
        if (!context.Prompter.Interactive) return null;
        // SAFE: 运行中的进程总有可执行文件路径。
        string tool = Environment.ProcessPath!;
        return OperatingSystem.IsWindows() ? RunAsAdministrator(tool, arguments, context) : RunWithSudo(tool, arguments, context);
    }

    // 提权后的进程不能把输出写回原控制台，改写到临时文件，结束后由本进程显示。
    private static int? RunAsAdministrator(string tool, IReadOnlyList<string> arguments, ToolContext context)
    {
        string output = Path.Combine(Path.GetTempPath(), $"shinano-tool-{Guid.NewGuid():N}.log");
        string[] all = [.. arguments, "--" + ToolContext.ELEVATED_SWITCH, "--" + ToolContext.ELEVATED_OUTPUT_OPTION, output];
        ProcessStartInfo info = new(tool)
        {
            UseShellExecute = true,
            Verb            = RUN_AS_VERB,
            WindowStyle     = ProcessWindowStyle.Hidden,
            Arguments       = string.Join(' ', all.Select(CommandRunner.QuoteWindows))
        };

        context.Log.Say("需要管理员权限，正在请求提权（请在 UAC 对话框中确认）……");
        try
        {
            using Process process = Process.Start(info) ?? throw new ToolException("无法以管理员身份启动工具");
            process.WaitForExit();
            if (File.Exists(output))
                foreach (string line in File.ReadAllLines(output))
                    Console.WriteLine(line);
            return process.ExitCode;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ERROR_CANCELLED)
        {
            context.Log.Warning("已拒绝提权");
            return null;
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    private static int? RunWithSudo(string tool, IReadOnlyList<string> arguments, ToolContext context)
    {
        string? sudo = SudoLocations.FirstOrDefault(File.Exists);
        if (sudo is null) return null;
        if (!context.Prompter.Confirm("需要 root 权限，是否用 sudo 执行这一步", null, fallback: true)) return null;

        ProcessStartInfo info = new(sudo) { UseShellExecute = false };
        foreach (string argument in (string[]) [tool, .. arguments, "--" + ToolContext.ELEVATED_SWITCH])
            info.ArgumentList.Add(argument);
        using Process process = Process.Start(info) ?? throw new ToolException($"无法启动 {SUDO}");
        process.WaitForExit();
        return process.ExitCode;
    }
}
