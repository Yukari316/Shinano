import { computed, ref } from 'vue'
import type { LogEntry } from './types'

const MAX_ENTRIES = 1000

/**
 * 实时日志：每个标签页只保持一个 SSE 连接，右侧日志面板与日志页共用。
 * 有订阅者且标签页可见时连接，否则断开；重连时服务端会重发缓冲，按序号去重。
 * 隐藏时断开是因为本机 HTTP 只能走 HTTP/1.1，浏览器对同一主机最多 6 个连接，
 * 多个后台标签页的长连接会占满连接数，使其他请求（包括页面代码块）一直排队。
 * 暂停时仍接收日志，只是显示停在暂停那一刻，恢复后一次补齐。
 */
export const logEntries = ref<LogEntry[]>([])
export const logsPaused = ref(false)
// 暂停那一刻的快照：按序号过滤会随缓冲裁剪逐渐变空，宿主重启后序号重新开始时也会把新日志漏进来。
const pausedEntries = ref<LogEntry[]>([])

/** 当前应显示的日志：暂停时为暂停那一刻的快照。 */
export const visibleLogs = computed(() => (logsPaused.value ? pausedEntries.value : logEntries.value))

export const warningCount = computed(() => logEntries.value.filter((entry) => entry.level === 'Warning').length)
export const errorCount = computed(
  () => logEntries.value.filter((entry) => entry.level === 'Error' || entry.level === 'Fatal').length
)

let source: EventSource | null = null
let subscribers = 0
let lastSequence = 0

function connect(): void {
  source = new EventSource('/api/logs/stream')
  // 服务端每次连接都会重发全部缓冲（与本地上限同为 1000 条），所以连上时清空重收；
  // 宿主重启后序号从 1 重新开始，不清空就会把新进程的日志当作重复全部丢弃。
  source.onopen = () => {
    lastSequence = 0
    logEntries.value = []
  }
  source.onmessage = (event: MessageEvent<string>) => {
    const entry = JSON.parse(event.data) as LogEntry
    if (entry.sequence <= lastSequence) return
    lastSequence = entry.sequence
    logEntries.value.push(entry)
    if (logEntries.value.length > MAX_ENTRIES) logEntries.value.splice(0, logEntries.value.length - MAX_ENTRIES)
  }
}

function syncConnection(): void {
  const wanted = subscribers > 0 && document.visibilityState === 'visible'
  if (wanted && source === null) connect()
  if (!wanted && source !== null) {
    source.close()
    source = null
  }
}

document.addEventListener('visibilitychange', syncConnection)

export function togglePause(): void {
  logsPaused.value = !logsPaused.value
  pausedEntries.value = logsPaused.value ? [...logEntries.value] : []
}

/** 订阅实时日志，返回取消订阅的函数。 */
export function subscribeLogs(): () => void {
  subscribers++
  syncConnection()
  let released = false
  return () => {
    if (released) return
    released = true
    subscribers--
    syncConnection()
  }
}
