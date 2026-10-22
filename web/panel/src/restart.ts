import { computed, ref } from 'vue'
import { api } from './api'
import type { PluginChange, RestartMode, RestartStatus } from './types'

const POLL_INTERVAL_MS = 2_000
/** 超过该时长仍未连上新进程时提示查看日志，但继续等待。 */
export const RESTART_SLOW_MS = 90_000

/** 待处理变更、重启方式与最近一次重启结果；插件管理页、侧边栏、总览与重启对话框共用。 */
export const restartStatus = ref<RestartStatus | null>(null)
export const pendingCount = computed(() => restartStatus.value?.pending.length ?? 0)
export const restartDialogOpen = ref(false)
/** 重启开始的时间；为 null 时没有进行中的重启。 */
export const restartStartedAt = ref<number | null>(null)

export async function refreshRestart(): Promise<void> {
  restartStatus.value = await api.get<RestartStatus>('/api/restart')
}

export function openRestartDialog(): void {
  restartDialogOpen.value = true
  refreshRestart().catch(() => undefined)
}

/**
 * 发起重启并等待新进程：旧进程退出后接口暂时不可用，连上实例 ID 不同且启动流程已结束的进程后刷新页面。
 * 会话签名密钥持久化在部署数据目录，新进程接受原有会话。
 */
export async function beginRestart(): Promise<void> {
  const { instanceId } = await api.post<{ instanceId: string }>('/api/restart')
  restartDialogOpen.value = false
  restartStartedAt.value = Date.now()
  const poll = async (): Promise<void> => {
    try {
      const status = await api.get<RestartStatus>('/api/restart')
      if (status.instanceId !== instanceId && status.phase !== 'Starting') {
        window.location.reload()
        return
      }
    } catch {
      // 重启期间连接失败是预期的，继续等待。
    }
    window.setTimeout(poll, POLL_INTERVAL_MS)
  }
  window.setTimeout(poll, POLL_INTERVAL_MS)
}

export function modeLabel(mode: RestartMode): string {
  return mode === 'Helper' ? '辅助程序' : '服务管理器'
}

export function describeChange(change: PluginChange): string {
  switch (change.kind) {
    case 'Install':
      return `安装 ${change.toVersion ?? ''}`
    case 'Upgrade':
      return `${change.fromVersion ?? '?'} → ${change.toVersion ?? '?'}`
    default:
      return `删除 ${change.fromVersion ?? ''}（保留插件数据）`
  }
}

/** 回滚后各项变更的去向，与通知卡片的说法一致。 */
export function describeRollback(change: PluginChange): string {
  switch (change.kind) {
    case 'Install':
      return `${change.toVersion ?? ''} → 已移除`
    case 'Upgrade':
      return `${change.toVersion ?? '?'} → 已还原为 ${change.fromVersion ?? '?'}`
    default:
      return `${change.fromVersion ?? ''} → 已恢复`
  }
}

export const CHANGE_KIND_LABEL: Record<PluginChange['kind'], string> = {
  Install: '安装',
  Upgrade: '升级',
  Remove: '删除'
}
