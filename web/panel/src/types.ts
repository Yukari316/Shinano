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
  statusPage: { url: string; loopbackOnly: boolean }
}

export interface BotStatus {
  connected: boolean
  selfId: number
  nickname?: string | null
  impl?: string | null
  groupCount?: number | null
  friendCount?: number | null
  groups?: BotGroup[] | null
}

export interface BotGroup {
  groupId: number
  name: string
  memberCount: number
  maxMemberCount: number
}

export interface PluginCommandType {
  name: string
  enabled: boolean
  commandCount: number
}

/** 插件运行状态：Pending 为已启用、等待机器人账号就绪；InitFailed 为已启用但初始化失败，可重试。 */
export type PluginStatus = 'Disabled' | 'Active' | 'Pending' | 'InitFailed' | 'LoadFailed'

export interface PluginSnapshot {
  id: string
  name: string
  version: string
  description: string
  /** 持久化的启用状态；是否已在响应见 status。 */
  enabled: boolean
  status: PluginStatus
  sortOrder: number
  error: string | null
  hasConfig: boolean
  hasSecrets: boolean
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

export type HostConfigFieldKind = 'Text' | 'Integer' | 'IntegerList' | 'Choice' | 'Boolean' | 'Secret'

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

/** 插件机密表单中的一项；不含机密的值。 */
export interface PluginSecretField {
  key: string
  label: string
  description: string
  editable: boolean
  configured: boolean
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

export type PluginChangeKind = 'Install' | 'Upgrade' | 'Remove'

export interface PluginChange {
  kind: PluginChangeKind
  pluginId: string
  name: string
  fromVersion: string | null
  toVersion: string | null
  purgeData: boolean
}

export interface PluginTables {
  global: string[]
  account: string[]
}

export interface PluginDataPreview {
  tables: PluginTables
  buckets: string[]
  secretsFile: boolean
  browserProfile: boolean
}

export type PluginPurgeState = 'Waiting' | 'Running' | 'Completed' | 'PartiallyFailed' | 'Cancelled'
export type PluginPurgeStepKind =
  | 'Tables'
  | 'SchemaVersions'
  | 'MediaReferences'
  | 'Buckets'
  | 'ConfigAndState'
  | 'Secrets'
  | 'BrowserProfile'
  | 'Leftovers'

export interface PluginPurgeStep {
  kind: PluginPurgeStepKind
  done: boolean
  summary: string | null
  error: string | null
}

export interface PluginPurge {
  pluginId: string
  name: string
  createdAt: string
  state: PluginPurgeState
  finishedAt: string | null
  steps: PluginPurgeStep[]
  possibleLeftovers: string[]
}

export interface PendingPluginChange {
  change: PluginChange
  stagedAt: string
  sizeBytes: number
}

export type StartupPhase = 'Starting' | 'Ready' | 'FaultMode'
export type RestartOutcome = 'Succeeded' | 'RolledBack' | 'Unverified'

export interface RestartResult {
  outcome: RestartOutcome
  reason: string | null
  finishedAt: string
  changes: PluginChange[]
}

export interface RestartStatus {
  instanceId: string
  phase: StartupPhase
  /** 是否有守护者；没有时重启只会退出，需要手动再启动。 */
  supervised: boolean
  restarting: boolean
  pending: PendingPluginChange[]
  lastResult: RestartResult | null
}
