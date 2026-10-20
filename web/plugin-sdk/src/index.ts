/**
 * Shinano 插件专有页面的契约。
 *
 * 插件包 `web/` 中的入口模块默认导出一个 Vue 组件；宿主以 `context` 属性传入 {@link PluginPageContext}。
 * 模块中的 `vue` 与 `@shinano/plugin-sdk` 由面板的 import map 提供，插件构建时应把两者声明为外部依赖。
 * 插件页面只在插件启用时出现，停用后宿主移除页面，插件 API 返回 404。
 */

/** 宿主传给插件页面的上下文。 */
export interface PluginPageContext {
  /** 插件 ID。 */
  readonly pluginId: string
  /**
   * 调用本插件的管理 API。`path` 相对 `/api/plugin-api/{插件 ID}`，以 `/` 开头；
   * 请求携带面板会话，会话失效时宿主跳转登录页。
   */
  api(path: string, init?: RequestInit): Promise<Response>
}

/** 插件管理 API 的路径前缀。 */
export const PLUGIN_API_PREFIX = '/api/plugin-api'

/** 创建插件页面上下文，供宿主使用。 */
export function createPluginPageContext(pluginId: string, onUnauthorized: () => void): PluginPageContext {
  return {
    pluginId,
    async api(path: string, init?: RequestInit): Promise<Response> {
      const response = await fetch(`${PLUGIN_API_PREFIX}/${encodeURIComponent(pluginId)}${path}`, {
        credentials: 'same-origin',
        ...init
      })
      if (response.status === 401) onUnauthorized()
      return response
    }
  }
}
