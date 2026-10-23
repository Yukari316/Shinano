export type HealthLevel = 'Normal' | 'Degraded' | 'Stopped'
export type MonitoredService = 'Host' | 'Database' | 'ObjectStorage' | 'Bot'
export type ProbeFailure =
  | 'NotConfigured'
  | 'Timeout'
  | 'ConnectionFailed'
  | 'AuthenticationFailed'
  | 'HttpStatus'
  | 'FaultMode'
  | 'Starting'
  | 'Unhealthy'
  | 'Error'

export interface StatusBar {
  start: string
  level: HealthLevel | null
  total: number
  stopped: number
}

export interface ServiceStatus {
  service: MonitoredService
  level: HealthLevel | null
  checkedAt: string | null
  latencyMs: number | null
  failure: ProbeFailure | null
  statusCode: number | null
  availability24h: number | null
  availability7d: number | null
  bars: StatusBar[]
}

export interface StatusReport {
  overall: HealthLevel | null
  generatedAt: string
  intervalSeconds: number
  configured: boolean
  timeZone: string
  services: ServiceStatus[]
}
