import type { HealthLevel } from './types'

const BYTE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB']
const BYTES_PER_UNIT = 1024

export function formatBytes(bytes: number): string {
  let value = bytes
  let unit = 0
  while (value >= BYTES_PER_UNIT && unit < BYTE_UNITS.length - 1) {
    value /= BYTES_PER_UNIT
    unit++
  }
  return `${value.toFixed(unit === 0 ? 0 : 1)} ${BYTE_UNITS[unit]}`
}

export function formatTime(value: string): string {
  return new Date(value).toLocaleString('zh-CN', { hour12: false })
}

export function formatNumber(value: number): string {
  return value.toLocaleString('zh-CN')
}

export const HEALTH_TEXT: Record<HealthLevel, string> = {
  Normal: '正常',
  Degraded: '降级',
  Stopped: '停止'
}

/** 健康等级对应 daisyUI 语义色。 */
export const HEALTH_BADGE: Record<HealthLevel, string> = {
  Normal: 'badge-success',
  Degraded: 'badge-warning',
  Stopped: 'badge-error'
}

/** 本地日期的 `YYYY-MM-DD` 表示，与统计接口返回的日期格式一致。 */
export function isoDate(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')
  return `${date.getFullYear()}-${month}-${day}`
}

/** 最近 `days` 天（含今天）的日期，按时间先后排列。 */
export function recentDates(days: number): string[] {
  const today = new Date()
  return Array.from({ length: days }, (_, index) => {
    const date = new Date(today)
    date.setDate(today.getDate() - (days - 1 - index))
    return isoDate(date)
  })
}

/** `YYYY-MM-DD` 的月-日简写。 */
export function shortDate(value: string): string {
  return value.slice(5)
}
