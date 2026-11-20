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

/** 插件管理 API 的完整前缀（`/api/plugin-api/{插件 ID}`），用于页面中直接引用的链接与图片地址。 */
export function pluginApiBase(pluginId: string): string {
  return `${PLUGIN_API_PREFIX}/${encodeURIComponent(pluginId)}`
}

/** 创建插件页面上下文，供宿主使用。 */
export function createPluginPageContext(pluginId: string, onUnauthorized: () => void): PluginPageContext {
  return {
    pluginId,
    async api(path: string, init?: RequestInit): Promise<Response> {
      const response = await fetch(`${pluginApiBase(pluginId)}${path}`, {
        credentials: 'same-origin',
        ...init
      })
      if (response.status === 401) onUnauthorized()
      return response
    }
  }
}

/** {@link callApi} 的结果。 */
export interface ApiResult<T> {
  /** 是否为成功状态码。 */
  readonly ok: boolean
  /** HTTP 状态码；请求未送达（网络错误）时为 0。 */
  readonly status: number
  /** 解析后的 JSON 响应；204 或响应不是 JSON 时为 null。失败时为错误响应本身。 */
  readonly body: T | null
  /** 失败时的说明：优先取响应的 `error`，其次 `detail`，否则为状态码说明；成功时为 null。 */
  readonly error: string | null
}

/**
 * 调用本插件的管理 API 并解析 JSON 响应。不抛出异常：网络错误与非成功状态都以 {@link ApiResult.error} 返回，
 * 调用方可以放心在之后复位加载中、上传中等状态。
 */
export async function callApi<T>(context: PluginPageContext, path: string, init?: RequestInit): Promise<ApiResult<T>> {
  let response: Response
  try {
    response = await context.api(path, init)
  } catch {
    return { ok: false, status: 0, body: null, error: '网络错误，请求未送达' }
  }

  const body = response.status === 204 ? null : ((await response.json().catch(() => null)) as T | null)
  if (response.ok) return { ok: true, status: response.status, body, error: null }
  const message = (body ?? {}) as { error?: string; detail?: string }
  return { ok: false, status: response.status, body, error: message.error ?? message.detail ?? `请求失败（${response.status}）` }
}

/** 以 JSON 为请求体的请求选项。 */
export function jsonBody(method: string, body: unknown): RequestInit {
  return { method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) }
}

/** 把 ISO 时间显示为本地时间（24 小时制）；没有值时为「—」。 */
export function formatTime(value: string | null | undefined): string {
  return value ? new Date(value).toLocaleString('zh-CN', { hour12: false }) : '—'
}
