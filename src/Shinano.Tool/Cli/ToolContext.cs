using Shinano.Tool.Deployment;

namespace Shinano.Tool.Cli;

/// <summary>一次工具调用的上下文。</summary>
public sealed class ToolContext
{
    /// <summary>以管理员身份重新启动的子进程标记：只执行需要权限的那一步，不写部署状态、不询问；由工具自己传入。</summary>
    public const string ELEVATED_SWITCH = "elevated";

    /// <summary>Windows 上提权子进程写输出的文件，由原进程读取并显示；由工具自己传入。</summary>
    public const string ELEVATED_OUTPUT_OPTION = "elevated-output";

    /// <summary>让其余项全部取默认值或现有值的开关。</summary>
    public const string ACCEPT_DEFAULTS_SWITCH = "accept-defaults";

    /// <summary>命令行参数。</summary>
    public required CommandArguments Arguments { get; init; }

    /// <summary>当前运行的工具所在的部署。</summary>
    public required DeploymentLayout Layout { get; init; }

    /// <summary>输出与操作记录。</summary>
    public required ToolLog Log { get; init; }

    /// <summary>取值与询问。</summary>
    public required Prompter Prompter { get; init; }

    /// <summary>本进程是否是以管理员身份重新启动的子进程。</summary>
    public bool Elevated => Arguments.Switch(ELEVATED_SWITCH);

    /// <summary>按参数创建上下文。</summary>
    public static ToolContext Create(CommandArguments arguments)
    {
        DeploymentLayout layout = DeploymentLayout.Current();
        string? mirror = arguments.Value(ELEVATED_OUTPUT_OPTION);
        bool interactive = !arguments.Switch(ELEVATED_SWITCH) && !arguments.Switch(ACCEPT_DEFAULTS_SWITCH) && !Console.IsInputRedirected;
        return new ToolContext
        {
            Arguments = arguments,
            Layout    = layout,
            Log       = new ToolLog(layout.ToolLogPath, mirror),
            Prompter  = new Prompter(arguments, interactive)
        };
    }
}
