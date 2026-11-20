/** NB2 运行环境与期望基础锁定文件的关系：Incomplete 为刚建立、启动时自动安装插件层；Outdated、Unmanaged 建议重建。 */
export type BaseState = 'Missing' | 'Current' | 'Incomplete' | 'Outdated' | 'Unmanaged'

/** NB2 进程状态。 */
export type ProcessState = 'Stopped' | 'Starting' | 'Running' | 'Stopping' | 'Failed' | 'Missing'

/** 传给 NB2 进程的环境变量（显示值）。 */
export interface EnvironmentVariable {
  name: string
  display: string
}

/** 进程状态。 */
export interface Status {
  state: ProcessState
  pid: number | null
  startedAt: string | null
  connected: boolean
  /** 自上次就绪（或手动启动）以来的自动重启次数。 */
  autoRestarts: number
  lastExitCode: number | null
  lastError: string | null
  ffmpegDirectory: string | null
  ffmpegVersion: string | null
  ffmpegError: string | null
  environment: EnvironmentVariable[]
  /** 环境与当前 NB2Bridge 期望的基础锁定文件的关系。 */
  baseState: BaseState
}

/** NB2 插件的最近一次错误。 */
export interface PluginError {
  type: string
  message: string
  time: number
}

/** NB2 进程内的统计。 */
export interface PluginStats {
  events: number
  runs: number
  errors: number
  apiCalls: number
  lastError: PluginError | null
}

/** NB2 进程内的插件状态。 */
export interface RemotePlugin {
  loaded: boolean
  name: string
  description: string
  matchers: number
  hasConfig: boolean
  stats: PluginStats
}

/** 已安装的 NB2 插件。 */
export interface Plugin {
  package: string
  module: string
  version: string
  fromWheel: boolean
  enabled: boolean
  installedAt: string
  remote: RemotePlugin | null
}

/** JSON Schema 中的一个字段（只取表单用到的部分）。 */
export interface SchemaProperty {
  title?: string
  description?: string
  type?: string | string[]
  enum?: unknown[]
  anyOf?: SchemaProperty[]
  items?: SchemaProperty
  $ref?: string
  default?: unknown
}

/** pydantic 生成的配置模型 JSON Schema。 */
export interface ConfigSchema {
  properties?: Record<string, SchemaProperty>
  $defs?: Record<string, SchemaProperty>
}

/** 配置表单。 */
export interface ConfigView {
  schema: ConfigSchema
  values: Record<string, unknown>
}

/** NB2 响应范围的会话类别。 */
export type ScopeKind = 'group' | 'private'

/** 一类会话的响应范围。 */
export interface Scope {
  /** 为 true 时为白名单，否则为黑名单。 */
  whitelist: boolean
  ids: number[]
}

/** 群聊与私聊的响应范围。 */
export type Scopes = Record<ScopeKind, Scope>

/** 选择范围时的候选会话；Bot 已不在该群或已不是好友时没有名称。 */
export interface ScopeCandidate {
  id: number
  name: string | null
}

/** 插件层一个包的版本变化；新增或移除的包另一侧为 null。 */
export interface VersionChange {
  package: string
  from: string | null
  to: string | null
}

/** 依赖变更（安装、卸载、重建）成功的结果。 */
export interface ChangeView {
  changes: VersionChange[]
}

/** 管理员约束文件的内容。 */
export interface ConstraintsView {
  text: string
}

/** 依赖变更失败的响应；conflict 表示与已装插件锁定的版本冲突，可确认后放宽重试。 */
export interface ChangeError {
  error: string
  conflict: boolean
}
