import type { HealthLevel } from './types'

const BYTE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB']
const BYTES_PER_UNIT = 1024
const DAY_MS = 86_400_000

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

/**
 * 某一时刻在部署时区中的日期，`YYYY-MM-DD`，与统计接口返回的日期格式一致（统计按部署时区分日）。
 * 时区未知或浏览器不认识（如 Windows 时区名）时按浏览器本地时区。
 */
export function isoDate(date: Date, timeZone?: string): string {
  try {
    // en-CA 的日期格式正是 YYYY-MM-DD。
    return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(date)
  } catch {
    return new Intl.DateTimeFormat('en-CA', { year: 'numeric', month: '2-digit', day: '2-digit' }).format(date)
  }
}

/** 部署时区中最近 `days` 天（含今天）的日期，按时间先后排列。 */
export function recentDates(days: number, timeZone?: string): string[] {
  const today = isoDate(new Date(), timeZone)
  // 在 UTC 中按日历日递减（UTC 没有夏令时，每天恰好 24 小时），不受浏览器时区影响。
  const base = Date.UTC(Number(today.slice(0, 4)), Number(today.slice(5, 7)) - 1, Number(today.slice(8, 10)))
  return Array.from({ length: days }, (_, index) => new Date(base - (days - 1 - index) * DAY_MS).toISOString().slice(0, 10))
}

/** `YYYY-MM-DD` 的月-日简写。 */
export function shortDate(value: string): string {
  return value.slice(5)
}
