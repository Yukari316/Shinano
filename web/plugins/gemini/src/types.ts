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
