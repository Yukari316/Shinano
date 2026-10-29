using System.Text;

namespace Shinano.Tool.Cli;

/// <summary>
/// 逐项取得配置值：命令行选项优先；否则交互运行时询问（直接回车取默认值），非交互运行时取默认值。
/// 敏感值不接受命令行选项，只从环境变量或隐藏输入读取，避免留在命令行历史中。
/// </summary>
public sealed class Prompter
{
    /// <summary>跳过确认的开关。</summary>
    public const string YES_SWITCH = "yes";

    private const string YES = "y";
    private const string NO  = "n";

    private readonly CommandArguments _arguments;

    /// <summary>创建取值入口。</summary>
    /// <param name="arguments">命令行参数。</param>
    /// <param name="interactive">是否可以询问；为 false 时一律取命令行选项或默认值。</param>
    public Prompter(CommandArguments arguments, bool interactive)
    {
        _arguments  = arguments;
        Interactive = interactive;
    }

    /// <summary>是否可以询问。</summary>
    public bool Interactive { get; }

    /// <summary>取得文本值。</summary>
    /// <param name="label">问题。</param>
    /// <param name="option">对应的命令行选项名。</param>
    /// <param name="fallback">默认值（已有配置时为现值）；为 null 时必须输入。</param>
    /// <param name="validate">返回错误原因，合法时返回 null。</param>
    /// <exception cref="ToolException">命令行给出的值不合法，或非交互运行时缺少必填值。</exception>
    public string Text(string label, string option, string? fallback, Func<string, string?>? validate = null)
    {
        if (_arguments.Value(option) is { } given)
        {
            string value = given.Trim();
            if (validate?.Invoke(value) is { } problem) throw new ToolException($"--{option}：{problem}");
            return value;
        }

        if (!Interactive)
        {
            if (fallback is null) throw new ToolException($"缺少必填项「{label}」，请用 --{option} 给出");
            if (validate?.Invoke(fallback) is { } problem) throw new ToolException($"「{label}」的现有值不合法：{problem}；请用 --{option} 给出");
            return fallback;
        }

        while (true)
        {
            Console.Write(fallback is { Length: > 0 } ? $"{label} [{fallback}]：" : $"{label}：");
            string input = (Console.ReadLine() ?? string.Empty).Trim();
            string value = input.Length == 0 ? fallback ?? string.Empty : input;
            if (value.Length == 0 && fallback is null)
            {
                Console.WriteLine("  此项必填");
                continue;
            }

            if (validate?.Invoke(value) is { } problem)
            {
                Console.WriteLine($"  {problem}");
                continue;
            }

            return value;
        }
    }

    /// <summary>取得是或否。</summary>
    /// <param name="label">问题。</param>
    /// <param name="option">对应的命令行选项名，取值 <c>yes</c> 或 <c>no</c>；为 null 时不接受命令行选项。</param>
    /// <param name="fallback">默认值。</param>
    public bool Confirm(string label, string? option, bool fallback)
    {
        if (option is not null && _arguments.Value(option) is { } given)
            return given.Trim().ToLowerInvariant() switch
            {
                "yes" or "true" or YES => true,
                "no" or "false" or NO  => false,
                _                      => throw new ToolException($"--{option} 只接受 yes 或 no")
            };

        if (!Interactive) return fallback;

        while (true)
        {
            Console.Write($"{label} [{(fallback ? "Y/n" : "y/N")}]：");
            string input = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
            if (input.Length == 0) return fallback;
            if (input is YES or "yes") return true;
            if (input is NO or "no") return false;
        }
    }

    /// <summary>取得敏感值。</summary>
    /// <param name="label">问题。</param>
    /// <param name="variable">提供该值的环境变量名。</param>
    /// <param name="configured">本地配置中是否已有值；有值时直接回车保持不变。</param>
    /// <param name="required">没有现值时是否必须给出。</param>
    /// <returns>新值；保持现值时为 null。</returns>
    /// <exception cref="ToolException">非交互运行时缺少必填值。</exception>
    public string? Secret(string label, string variable, bool configured, bool required)
    {
        if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } given) return given;
        if (!Interactive)
        {
            if (configured || !required) return null;
            throw new ToolException($"缺少必填项「{label}」，请通过环境变量 {variable} 给出");
        }

        while (true)
        {
            Console.Write(configured ? $"{label}（已配置，回车保持）：" : $"{label}：");
            string input = ReadHidden();
            if (input.Length > 0) return input;
            if (configured || !required) return null;
            Console.WriteLine("  此项必填");
        }
    }

    /// <summary>在交互运行时等待确认，非交互运行时直接通过。</summary>
    /// <param name="label">问题。</param>
    /// <exception cref="ToolException">管理员选择不继续。</exception>
    public void RequireConfirmation(string label)
    {
        if (_arguments.Switch(YES_SWITCH) || !Interactive) return;
        if (!Confirm(label, null, fallback: true)) throw new ToolException("已取消");
    }

    private static string ReadHidden()
    {
        if (Console.IsInputRedirected) return (Console.ReadLine() ?? string.Empty).Trim();

        StringBuilder buffer = new();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return buffer.ToString();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (buffer.Length > 0) buffer.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar)) buffer.Append(key.KeyChar);
        }
    }
}
