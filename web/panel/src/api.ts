import router from './router'

/** 面板 API 返回的错误，`errors` 为校验失败的各项原因。 */
export class ApiError extends Error {
  readonly status: number
  readonly errors: string[]

  constructor(status: number, message: string, errors: string[]) {
    super(message)
    this.status = status
    this.errors = errors
  }
}

/**
 * 会话失效时跳转登录页，并记住当前页面以便登录后返回。返回地址取浏览器地址而不是路由状态：
 * 首次导航完成前侧边栏等请求就可能收到 401，此时路由仍处于起始位置 `/`。
 */
export function redirectToLogin(): void {
  if (router.currentRoute.value.name === 'login' || window.location.pathname === '/login') return
  const redirect = `${window.location.pathname}${window.location.search}${window.location.hash}`
  void router.push({ name: 'login', query: { redirect } })
}

/** 调用面板 API；401 时跳转登录页，非成功状态抛出 {@link ApiError}，204 返回 undefined。 */
export async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, { credentials: 'same-origin', ...init })
  if (response.status === 401 && !path.startsWith('/api/auth/login')) {
    redirectToLogin()
    throw new ApiError(401, '登录已失效', [])
  }

  if (!response.ok) {
    let message = `请求失败（${response.status}）`
    let errors: string[] = []
    try {
      const body = (await response.json()) as { message?: string; errors?: string[] }
      message = body.message ?? message
      errors = body.errors ?? []
    } catch {
      // 响应不是 JSON 时使用状态码说明。
    }
    throw new ApiError(response.status, message, errors)
  }

  if (response.status === 204) return undefined as T
  return (await response.json()) as T
}

function withJson(method: string, body: unknown): RequestInit {
  return {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  }
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) => request<T>(path, body === undefined ? { method: 'POST' } : withJson('POST', body)),
  put: <T>(path: string, body: unknown) => request<T>(path, withJson('PUT', body)),
  delete: <T>(path: string) => request<T>(path, { method: 'DELETE' })
}

/** 把错误转换为面向用户的说明。 */
export function describeError(error: unknown): string {
  if (error instanceof ApiError) return error.errors.length > 0 ? `${error.message}：${error.errors.join('；')}` : error.message
  return error instanceof Error ? error.message : String(error)
}
