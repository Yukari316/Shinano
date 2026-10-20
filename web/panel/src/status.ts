import { ref } from 'vue'
import { api } from './api'
import type { Overview } from './types'

const REFRESH_INTERVAL_MS = 30_000

/** 总览状态，侧边栏与总览页共用；登录后每 30 秒刷新一次，记录最近一次成功获取的时间。 */
export const overview = ref<Overview | null>(null)
export const overviewUpdatedAt = ref<Date | null>(null)

let timer: number | undefined

export async function refreshOverview(): Promise<void> {
  overview.value = await api.get<Overview>('/api/overview')
  overviewUpdatedAt.value = new Date()
}

export function startOverviewPolling(): void {
  if (timer !== undefined) return
  refreshOverview().catch(() => undefined)
  timer = window.setInterval(() => refreshOverview().catch(() => undefined), REFRESH_INTERVAL_MS)
}

export function stopOverviewPolling(): void {
  if (timer === undefined) return
  window.clearInterval(timer)
  timer = undefined
}
