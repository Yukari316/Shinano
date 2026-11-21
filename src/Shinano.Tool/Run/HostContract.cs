namespace Shinano.Tool.Run;

/// <summary>工具与宿主进程之间的约定。工具不引用主工程，这些值与宿主必须一致，由契约测试核对。</summary>
public static class HostContract
{
    /// <summary>宿主请求守护者重启时的退出码。</summary>
    public const int RESTART_EXIT_CODE = 75;

    /// <summary>告诉宿主自己有守护者的环境变量。</summary>
    public const string SUPERVISED_VARIABLE = "SHINANO_SUPERVISED";

    /// <summary><see cref="SUPERVISED_VARIABLE" /> 的取值。</summary>
    public const string SUPERVISED_VALUE = "1";

    /// <summary>宿主的就绪接口，只响应本机请求。</summary>
    public const string READINESS_PATH = "/healthz";

    /// <summary>就绪接口响应中启动阶段的字段名。</summary>
    public const string PHASE_PROPERTY = "phase";

    /// <summary>
    /// 重启时宿主写入的插件包变更计划，相对部署数据目录。重启后存在计划表示本次带插件包变更，新进程未能就绪时需要再启动一次，
    /// 由宿主还原上一套插件包。
    /// </summary>
    public const string RESTART_PLAN_PATH = "restart/plan.json";

    /// <summary>宿主完成插件包变更重启后写入的结果文件，相对部署数据目录。</summary>
    public const string RESTART_RESULT_PATH = "restart/result.json";

    /// <summary>表示启动流程已结束的阶段；故障模式与插件包无关，不触发再启动。</summary>
    public static readonly IReadOnlyList<string> FinishedPhases = ["Ready", "FaultMode"];
}
