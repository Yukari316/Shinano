using System.Reflection;
using System.Runtime.Loader;

namespace Shinano.Host.Plugins;

/// <summary>
/// 一个插件的程序集加载上下文。宿主已有的程序集（Framework 契约、Sora、SqlSugar、Autofac 等）
/// 一律使用默认上下文中的同一份，保证插件与宿主看到同一个类型；其余依赖从插件目录加载。
/// 不可回收：插件包的增删与升级在进程重启后生效。
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver _resolver;

    /// <summary>创建上下文。</summary>
    /// <param name="pluginId">插件 ID，作为上下文名称。</param>
    /// <param name="entryAssemblyPath">入口程序集的完整路径，其 <c>.deps.json</c> 用于解析私有依赖。</param>
    public PluginLoadContext(string pluginId, string entryAssemblyPath)
        : base($"plugin:{pluginId}", isCollectible: false)
    {
        _resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    /// <inheritdoc />
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        try
        {
            return Default.LoadFromAssemblyName(assemblyName);
        }
        catch (FileNotFoundException)
        {
            // 宿主没有该程序集，按插件自己的依赖解析。
        }

        string? path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    /// <inheritdoc />
    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
