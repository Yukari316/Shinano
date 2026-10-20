/** 健康等级，与宿主 HealthLevel 一致。 */
export type HealthLevel = 'Normal' | 'Degraded' | 'Stopped'

export interface HealthComponentStatus {
  component: string
  name: string
  level: HealthLevel
  detail: string
  since: string
}

export interface Overview {
  overall: HealthLevel
  components: HealthComponentStatus[]
  bot: { connected: boolean; selfId: number }
  plugins: { total: number; enabled: number; failed: number }
  logs: { usedBytes: number; limitMiB: number }
}

export interface BotStatus {
  connected: boolean
  selfId: number
  nickname?: string | null
  impl?: string | null
  groupCount?: number | null
  friendCount?: number | null
}

export interface PluginCommandType {
  name: string
  enabled: boolean
  commandCount: number
}

export interface PluginSnapshot {
  id: string
  name: string
  version: string
  description: string
  enabled: boolean
  sortOrder: number
  error: string | null
  hasConfig: boolean
  pageTitle: string | null
  pageEntry: string | null
  commandTypes: PluginCommandType[]
}

/** 宿主由配置类型导出的 JSON Schema 中面板用到的部分。 */
export interface JsonSchema {
  type?: string | string[]
  title?: string
  description?: string
  'x-order'?: number
  enum?: (string | number | null)[]
  properties?: Record<string, JsonSchema>
  items?: JsonSchema
  required?: string[]
  minimum?: number
  maximum?: number
  minLength?: number
  maxLength?: number
  pattern?: string
}

export interface PluginConfigForm {
  schema: JsonSchema
  value: Record<string, unknown>
}

export interface StatTotals {
  messages: number
  chars: number
  images: number
  audios: number
  videos: number
  files: number
}

export interface DailyStat extends StatTotals {
  date: string
}

export interface ConversationStat extends StatTotals {
  type: 'Friend' | 'Group' | 'Temp'
  conversationId: number
  name: string | null
  senders: number
}

export interface SenderStat extends StatTotals {
  senderId: number
  name: string | null
  isBot: boolean
}

export interface ArchiveGap {
  cause: 'DatabaseOffline' | 'BotDisconnected' | 'Backlog'
  startedAt: string
  endedAt: string
}

export type LogSegmentKind = 'Text' | 'String' | 'Number' | 'Boolean' | 'Null' | 'Other'

export interface LogSegment {
  text: string
  kind: LogSegmentKind
}

export interface LogEntry {
  sequence: number
  timestamp: string
  level: string
  category: string
  message: string
  segments: LogSegment[]
  exception?: string | null
}

export interface LogFile {
  name: string
  size: number
  modified: string
}

export type HostConfigFieldKind = 'Text' | 'Integer' | 'IntegerList' | 'Choice' | 'Secret'

export interface HostConfigField {
  section: string
  key: string
  label: string
  hint: string
  kind: HostConfigFieldKind
  minimum: number | null
  maximum: number | null
  options: string[]
  optional: boolean
}

export interface HostConfigEntry {
  field: HostConfigField
  value: string | number | boolean | number[] | null
}

export interface Appearance {
  panelOpacity: number
  cardOpacity: number
  veilOpacity: number
  blur: boolean
  backgroundFile: string | null
}
