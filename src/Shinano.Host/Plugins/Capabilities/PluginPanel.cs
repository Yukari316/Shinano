using Shinano.Framework.Plugins.Capabilities;
using Shinano.Host.Panel.Plugins;

namespace Shinano.Host.Plugins.Capabilities;

/// <summary>插件专有面板的宿主实现：记录页面入口，把管理 API 登记到 <see cref="PluginEndpointDataSource" />。</summary>
internal sealed class PluginPanel : IPluginPanel
{
    /// <summary>插件前端资源目录名。</summary>
    public const string WEB_DIRECTORY_NAME = "web";

    private readonly string                   _pluginId;
    private readonly string                   _webRoot;
    private readonly PluginEndpointDataSource _endpoints;
    private readonly Func<bool>               _isActive;

    /// <summary>创建专有面板。</summary>
    /// <param name="pluginId">插件 ID。</param>
    /// <param name="pluginDirectory">插件包目录。</param>
    /// <param name="endpoints">插件 API 的端点来源。</param>
    /// <param name="isActive">插件当前是否启用。</param>
    public PluginPanel(string pluginId, string pluginDirectory, PluginEndpointDataSource endpoints, Func<bool> isActive)
    {
        _pluginId  = pluginId;
        _webRoot   = Path.GetFullPath(Path.Combine(pluginDirectory, WEB_DIRECTORY_NAME)) + Path.DirectorySeparatorChar;
        _endpoints = endpoints;
        _isActive  = isActive;
    }

    /// <summary>页面标题；未登记页面时为 null。</summary>
    public string? Title { get; private set; }

    /// <summary>入口模块相对 <c>web/</c> 的路径；未登记页面时为 null。</summary>
    public string? Entry { get; private set; }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">重复登记页面。</exception>
    /// <exception cref="FileNotFoundException">入口模块不存在或位于 <c>web/</c> 之外。</exception>
    public void UsePage(string title, string entry)
    {
        if (Title is not null) throw new InvalidOperationException("每个插件只能登记一个专有页面");
        if (ResolveWebFile(entry) is null) throw new FileNotFoundException($"插件 {_pluginId} 的页面入口不存在：{entry}");
        Title = title;
        Entry = entry;
    }

    /// <inheritdoc />
    public void MapApi(Action<IEndpointRouteBuilder> map) => _endpoints.Map(_pluginId, _isActive, map);

    /// <summary>返回 <c>web/</c> 下的文件完整路径；路径越界或文件不存在时返回 null。</summary>
    public string? ResolveWebFile(string relativePath)
    {
        string path = Path.GetFullPath(Path.Combine(_webRoot, relativePath));
        return path.StartsWith(_webRoot, StringComparison.Ordinal) && File.Exists(path) ? path : null;
    }
}
