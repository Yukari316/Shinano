export type ScopeKind = 'group' | 'private'

export interface Scope {
  ids: number[]
}

export interface Scopes {
  group: Scope
  private: Scope
}

export interface ScopeCandidate {
  id: number
  name: string | null
}

export interface ModelSettings {
  model: string
  effort: string
  allowedEfforts: string[]
  hasApiKey: boolean
}

export interface ModelOption {
  id: string
  displayName: string
  description: string
  thinking: boolean | null
  allowedEfforts: string[]
}

export interface UsageMetrics {
  requestCount: number
  successCount: number
  failedCount: number
  promptTokens: number
  cachedTokens: number
  candidatesTokens: number
  thoughtsTokens: number
  totalTokens: number
  cacheHitRate: number
  costUsd: number
}

export interface ModelUsageRow {
  model: string
  metrics: UsageMetrics
}

export interface DailyUsageRow {
  date: string
  metrics: UsageMetrics
}

export interface UsageSummary {
  days: number
  totals: UsageMetrics
  byModel: ModelUsageRow[]
  daily: DailyUsageRow[]
}
