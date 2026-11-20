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
