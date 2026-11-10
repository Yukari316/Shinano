namespace Shinano.Host.Plugins.Models;

/// <summary>插件数据清除中一个步骤的结果。</summary>
public sealed class PluginPurgeStep
{
    /// <summary>步骤。</summary>
    public required PluginPurgeStepKind Kind { get; init; }

    /// <summary>是否已完成；未完成的步骤在下次执行时重试。</summary>
    public bool Done { get; set; }

    /// <summary>完成时的结果说明（删除数量等），面向管理员显示。</summary>
    public string? Summary { get; set; }

    /// <summary>最近一次失败的原因；成功后清除。</summary>
    public string? Error { get; set; }
}
