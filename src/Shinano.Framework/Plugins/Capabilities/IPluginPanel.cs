using Microsoft.AspNetCore.Routing;

namespace Shinano.Framework.Plugins.Capabilities;

/// <summary>
/// 插件专有面板：插件包 <c>web/</c> 中预先构建的浏览器 ESM 页面，以及插件自己的管理 API。
/// 只在 <see cref="IShinanoPlugin.InitializeAsync" /> 中登记；插件启用时出现在管理面板，停用时页面与 API 都不可访问。
/// </summary>
/// <remarks>
/// 页面入口模块的默认导出是一个 Vue 组件，宿主以 <c>context</c> 属性传入插件 ID 与调用插件 API 的方法；
/// 模块中的 <c>vue</c> 与 <c>@shinano/plugin-sdk</c> 由面板的 import map 提供，插件不得自带 Vue 运行时。
/// </remarks>
public interface IPluginPanel
{
    /// <summary>登记专有页面，每个插件最多一个。</summary>
    /// <param name="title">侧边栏中的页面标题。</param>
    /// <param name="entry">入口模块相对插件包 <c>web/</c> 目录的路径，使用 <c>/</c> 分隔。</param>
    void UsePage(string title, string entry);

    /// <summary>
    /// 登记管理 API。<paramref name="map" /> 收到的路由构建器以 <c>/api/plugin-api/{插件 ID}</c> 为前缀，
    /// 已要求面板登录；插件停用期间这些路由返回 404。可多次调用。
    /// </summary>
    void MapApi(Action<IEndpointRouteBuilder> map);
}
