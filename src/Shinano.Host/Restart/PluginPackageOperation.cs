using Shinano.Host.Packages;

namespace Shinano.Host.Restart;

/// <summary>重启计划中对一个插件目录的操作。</summary>
public sealed class PluginPackageOperation
{
    /// <summary>变更内容。</summary>
    public required PluginChange Change { get; init; }

    /// <summary>暂存的插件包目录；删除时为 null。</summary>
    public string? StagedDirectory { get; init; }

    /// <summary>
    /// 插件目录下的目标目录：升级与删除为已安装的目录，安装为 <c>&lt;插件目录&gt;/&lt;id&gt;</c>，建立计划时该目录不存在。
    /// </summary>
    public required string TargetDirectory { get; init; }
}
