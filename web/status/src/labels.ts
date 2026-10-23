import type { HealthLevel, MonitoredService, ProbeFailure, ServiceStatus } from './types'

export const SERVICES: Record<MonitoredService, { name: string; description: string }> = {
  Host: { name: 'Shinano 主进程', description: 'Bot、管理面板与插件' },
  Database: { name: 'PostgreSQL', description: '数据库' },
  ObjectStorage: { name: 'RustFS', description: '媒体存储' },
  Bot: { name: 'LLBot', description: 'QQ 协议端（Milky）' }
}

/** 等级的文字与 daisyUI 语义色；状态同时以图标与文字表达，不只依赖颜色。 */
export const LEVELS: Record<HealthLevel, { label: string; badge: string; bar: string; text: string }> = {
  Normal: { label: '正常', badge: 'badge-success', bar: 'bg-success', text: 'text-success-strong' },
  Degraded: { label: '降级', badge: 'badge-warning', bar: 'bg-warning', text: 'text-warning-strong' },
  Stopped: { label: '停止', badge: 'badge-error', bar: 'bg-error', text: 'text-error-strong' }
}

export const OVERALL: Record<HealthLevel, string> = {
  Normal: '全部服务正常',
  Degraded: '部分服务降级',
  Stopped: '部分服务停止'
}

const FAILURES: Record<ProbeFailure, string> = {
  NotConfigured: '部署配置中缺少该服务的地址或凭据',
  Timeout: '响应超时',
  ConnectionFailed: '无法连接',
  AuthenticationFailed: '认证失败',
  HttpStatus: 'HTTP 错误',
  FaultMode: '主进程处于故障模式',
  Starting: '主进程正在启动',
  Unhealthy: '主进程报告部分功能异常',
  Error: '探测出错'
}

export function describeFailure(status: ServiceStatus): string | null {
  if (status.failure === null) return null
  return status.failure === 'HttpStatus' && status.statusCode !== null ? `HTTP ${status.statusCode}` : FAILURES[status.failure]
}

export function percent(value: number | null): string {
  if (value === null) return '—'
  if (value === 1) return '100%'
  // 向下取两位小数，不把 99.996% 显示成 100%。
  return `${(Math.floor(value * 10_000) / 100).toFixed(2)}%`
}
