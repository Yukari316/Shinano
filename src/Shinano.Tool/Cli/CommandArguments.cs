using Shinano.Tool.Setup;

namespace Shinano.Tool.Cli;

/// <summary>
/// 命令行参数：位置参数（子命令与动作）与 <c>--名称 值</c>、<c>--名称=值</c> 形式的选项；开关只写名称。
/// 两种脚本入口都原样透传参数，PowerShell 中同样使用 <c>--名称</c>。
/// </summary>
public sealed class CommandArguments
{
    private const string OPTION_PREFIX = "--";

    // 不带值的开关；其余选项都需要值。
    private static readonly HashSet<string> SwitchNames =
        [ToolContext.ACCEPT_DEFAULTS_SWITCH, ToolContext.ELEVATED_SWITCH, Prompter.YES_SWITCH, InitCommand.NO_START_SWITCH];

    private readonly List<string>               _positionals = [];
    private readonly Dictionary<string, string> _values      = new(StringComparer.Ordinal);
    private readonly HashSet<string>            _switches    = new(StringComparer.Ordinal);

    private CommandArguments()
    {
    }

    /// <summary>位置参数，第一个为子命令。</summary>
    public IReadOnlyList<string> Positionals => _positionals;

    /// <summary>解析参数。</summary>
    /// <exception cref="ToolException">选项缺少值或重复。</exception>
    public static CommandArguments Parse(IReadOnlyList<string> args)
    {
        CommandArguments result = new();
        for (int index = 0; index < args.Count; index++)
        {
            string token = args[index];
            if (!token.StartsWith(OPTION_PREFIX, StringComparison.Ordinal))
            {
                result._positionals.Add(token);
                continue;
            }

            string body = token[OPTION_PREFIX.Length..];
            int equals = body.IndexOf('=');
            string name = equals < 0 ? body : body[..equals];
            if (SwitchNames.Contains(name) && equals < 0)
            {
                result._switches.Add(name);
                continue;
            }

            string value;
            if (equals >= 0) value = body[(equals + 1)..];
            else if (index + 1 < args.Count) value = args[++index];
            else throw new ToolException($"选项 --{name} 缺少值");

            if (!result._values.TryAdd(name, value)) throw new ToolException($"选项 --{name} 重复");
        }

        return result;
    }

    /// <summary>选项的值；未给出时为 null。</summary>
    public string? Value(string name) => _values.GetValueOrDefault(name);

    /// <summary>开关是否给出。</summary>
    public bool Switch(string name) => _switches.Contains(name);

    /// <summary>拒绝拼错或不适用于该命令的选项，避免它们被静默忽略。</summary>
    /// <param name="allowed">该命令接受的选项与开关名称。</param>
    /// <exception cref="ToolException">存在不在 <paramref name="allowed" /> 中的选项。</exception>
    public void RequireKnown(IReadOnlyCollection<string> allowed)
    {
        string[] unknown = [.. _values.Keys.Concat(_switches).Where(name => !allowed.Contains(name)).Order(StringComparer.Ordinal)];
        if (unknown.Length > 0)
            throw new ToolException($"不适用于该命令的选项：{string.Join("、", unknown.Select(name => OPTION_PREFIX + name))}");
    }
}
