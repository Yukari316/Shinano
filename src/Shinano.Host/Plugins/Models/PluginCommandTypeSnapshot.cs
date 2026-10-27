namespace Shinano.Host.Plugins.Models;

/// <summary>插件内一个指令类型的状态与指令数量。</summary>
public sealed class PluginCommandTypeSnapshot
{
    /// <summary>类型名。</summary>
    public required string Name { get; init; }

    /// <summary>指令能否匹配（程序集与类型都已启用）。</summary>
    public required bool Enabled { get; init; }

    /// <summary>指令数量。</summary>
    public required int CommandCount { get; init; }
}
