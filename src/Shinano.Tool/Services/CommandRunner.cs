using System.Diagnostics;
using System.Text;
using Shinano.Tool.Cli;
using Shinano.Tool.Deployment;

namespace Shinano.Tool.Services;

/// <summary>执行与输出 <see cref="SystemCommand" />。</summary>
public static class CommandRunner
{
    private const int MAX_OUTPUT_LENGTH = 2000;

    /// <summary>执行一条命令并记录请求与结果。</summary>
    /// <exception cref="ToolException">命令无法启动或以不接受的退出码结束；消息含命令输出。</exception>
    public static void Run(SystemCommand command, ToolLog log)
    {
        log.Info($"执行：{command.Description}（{Render(command)}）");
        (int exitCode, string output) = Capture(command.FileName, command.Arguments);
        if (exitCode == 0 || command.AcceptedExitCodes.Contains(exitCode))
        {
            log.Info($"完成：{command.Description}");
            return;
        }

        log.Error($"失败：{command.Description}，退出码 {exitCode}：{Truncate(output)}");
        throw new ToolException($"{command.Description}失败（退出码 {exitCode}），详见上方输出与 tool.log");
    }

    /// <summary>执行查询命令，取得退出码与合并后的输出；不记录。</summary>
    /// <exception cref="ToolException">命令无法启动。</exception>
    public static (int ExitCode, string Output) Capture(string fileName, IReadOnlyList<string> arguments)
    {
        ProcessStartInfo info = new(fileName)
        {
            UseShellExecute        = false,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            using Process process = Process.Start(info) ?? throw new ToolException($"无法启动 {fileName}");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
            Task<string> standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return (process.ExitCode, (standardOutput.Result + standardError.Result).Trim());
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new ToolException($"无法启动 {fileName}：{exception.Message}");
        }
    }

    /// <summary>命令的文本形式：Windows 上可在管理员命令提示符（cmd）中执行，Linux 上加 <c>sudo</c> 执行。</summary>
    public static string Render(SystemCommand command) =>
        OperatingSystem.IsWindows()
            ? string.Join(' ', [QuoteWindows(command.FileName), .. command.Arguments.Select(QuoteWindows)])
            : string.Join(' ', [QuotePosix(command.FileName), .. command.Arguments.Select(QuotePosix)]);

    /// <summary>按 Windows 命令行规则给一个参数加引号；不含空白与引号时原样返回。</summary>
    public static string QuoteWindows(string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"')) return argument;

        StringBuilder builder = new("\"");
        int backslashes = 0;
        foreach (char character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            // 引号前的反斜杠需要加倍，引号本身转义。
            builder.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
            builder.Append(character);
            backslashes = 0;
        }

        // 结尾的反斜杠后紧跟闭合引号，同样需要加倍。
        builder.Append('\\', backslashes * 2);
        return builder.Append('"').ToString();
    }

    private static string QuotePosix(string argument) =>
        argument.Length > 0 && argument.All(character => char.IsAsciiLetterOrDigit(character) || "-_./:=@%+,".Contains(character))
            ? argument
            : $"'{argument.Replace("'", "'\\''", StringComparison.Ordinal)}'";

    private static string Truncate(string text) => text.Length <= MAX_OUTPUT_LENGTH ? text : text[..MAX_OUTPUT_LENGTH] + "…";
}

/// <summary>一条修改系统状态的命令。有权限时由工具执行，否则原样输出给管理员执行。</summary>
public sealed class SystemCommand
{
    /// <summary>可执行文件。</summary>
    public required string FileName { get; init; }

    /// <summary>参数，每项一个参数，不需要预先加引号。</summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>这条命令做什么，写入操作记录。</summary>
    public required string Description { get; init; }

    /// <summary>除 0 以外同样视为成功的退出码，如服务已在运行时再次启动。</summary>
    public IReadOnlyList<int> AcceptedExitCodes { get; init; } = [];
}
