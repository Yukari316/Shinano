using System.Reflection;
using Autofac;
using Shinano.Framework.Plugins;
using Sora.Command;

namespace Shinano.Host.Plugins;

/// <summary>一个已发现插件的运行时状态，只由 <see cref="PluginRuntime" /> 在其锁内修改。</summary>
internal sealed class LoadedPlugin
{
    /// <summary>清单。</summary>
    public required PluginManifest Manifest { get; init; }

    /// <summary>插件包目录。</summary>
    public required string Directory { get; init; }

    /// <summary>入口程序集；加载失败时为 null。</summary>
    public Assembly? Assembly { get; set; }

    /// <summary>插件入口实例。</summary>
    public IShinanoPlugin? Instance { get; set; }

    /// <summary>插件容器。</summary>
    public IContainer? Container { get; set; }

    /// <summary>能力集合。</summary>
    public PluginContext? Context { get; set; }

    /// <summary>入口程序集中需要实例的指令组类型。</summary>
    public IReadOnlyList<Type> CommandTypes { get; set; } = [];

    /// <summary>Sora 程序集登记；首次启用后存在。</summary>
    public AssemblyRegistration? Registration { get; set; }

    /// <summary>是否已完成首次初始化。</summary>
    public bool Initialized { get; set; }

    /// <summary>持久化的启用状态。</summary>
    public bool Enabled { get; set; }

    /// <summary>持久化的顺序。</summary>
    public int SortOrder { get; set; }

    /// <summary>加载或初始化失败的原因；失败的插件在进程重启前不能启用。</summary>
    public string? Error { get; set; }
}
