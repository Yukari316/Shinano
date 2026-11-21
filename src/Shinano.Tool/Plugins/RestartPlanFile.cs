using System.Text.Json;
using System.Text.Json.Serialization;
using Shinano.Tool.Cli;
using Shinano.Tool.Run;

namespace Shinano.Tool.Plugins;

/// <summary>
/// 宿主重启计划（<c>restart/plan.json</c>）与重启结果（<c>restart/result.json</c>）的读写，格式与宿主一致。
/// </summary>
public static class RestartPlanFile
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    /// <summary>重启计划文件的完整路径。</summary>
    public static string PlanPath(string dataDirectory) => Path.Combine(dataDirectory, HostContract.RESTART_PLAN_PATH);

    /// <summary>重启结果文件的完整路径。</summary>
    public static string ResultPath(string dataDirectory) => Path.Combine(dataDirectory, HostContract.RESTART_RESULT_PATH);

    /// <summary>部署数据目录中是否存在未完成的重启计划。</summary>
    public static bool Exists(string dataDirectory) => File.Exists(PlanPath(dataDirectory));

    /// <summary>原子写入重启计划。</summary>
    public static void WritePlan(string dataDirectory, ToolRestartPlan plan)
    {
        string path = PlanPath(dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? dataDirectory);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(plan, JsonOptions));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>读取最近一次重启结果；文件不存在时为 null。</summary>
    /// <exception cref="ToolException">结果文件无法解析。</exception>
    public static ToolRestartResult? ReadResult(string dataDirectory)
    {
        string path = ResultPath(dataDirectory);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<ToolRestartResult>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ToolException($"重启结果文件 {path} 无法解析：{exception.Message}");
        }
    }
}

/// <summary>与宿主 <c>RestartPlan</c> 一致的重启计划。</summary>
public sealed class ToolRestartPlan
{
    /// <summary>发起时间（UTC）。</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>进度；工具只写入 <see cref="ToolRestartPlanState.Pending" />。</summary>
    public ToolRestartPlanState State { get; set; } = ToolRestartPlanState.Pending;

    /// <summary>加载失败的原因。</summary>
    public string? FailureReason { get; set; }

    /// <summary>按顺序执行的操作。</summary>
    public required List<ToolPluginPackageOperation> Operations { get; init; }
}

/// <summary>与宿主 <c>RestartPlanState</c> 一致的计划状态。</summary>
public enum ToolRestartPlanState
{
    /// <summary>等待新进程启动时应用。</summary>
    Pending,

    /// <summary>正在应用。</summary>
    Applying,

    /// <summary>已应用，等待加载验证。</summary>
    Applied,

    /// <summary>加载失败，等待下一次启动还原。</summary>
    LoadFailed
}

/// <summary>与宿主 <c>PluginPackageOperation</c> 一致的单个插件目录操作。</summary>
public sealed class ToolPluginPackageOperation
{
    /// <summary>变更内容。</summary>
    public required StagedChangeDetail Change { get; init; }

    /// <summary>暂存的插件包目录。</summary>
    public string? StagedDirectory { get; init; }

    /// <summary>插件目录下的目标目录。</summary>
    public required string TargetDirectory { get; init; }
}

/// <summary>与宿主 <c>RestartResult</c> 一致的重启结果。</summary>
public sealed class ToolRestartResult
{
    /// <summary>结果。</summary>
    public required ToolRestartOutcome Outcome { get; init; }

    /// <summary>回滚或未验证的原因；成功时为 null。</summary>
    public string? Reason { get; init; }

    /// <summary>完成时间（UTC）。</summary>
    public required DateTimeOffset FinishedAt { get; init; }

    /// <summary>本次重启涉及的变更。</summary>
    public required List<StagedChangeDetail> Changes { get; init; }
}

/// <summary>与宿主 <c>RestartOutcome</c> 一致的结果枚举。</summary>
public enum ToolRestartOutcome
{
    /// <summary>变更的插件包全部加载成功。</summary>
    Succeeded,

    /// <summary>新版本未能就绪，已还原上一套插件包。</summary>
    RolledBack,

    /// <summary>基础设施处于故障模式，插件包已应用但未经加载验证。</summary>
    Unverified
}
