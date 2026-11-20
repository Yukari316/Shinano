<script setup lang="ts">
import { PhArrowsClockwise, PhGear, PhPencilSimple, PhPlay, PhStop, PhTrash, PhUploadSimple, PhWrench, PhX } from '@phosphor-icons/vue'
import { type ApiResult, callApi, formatTime, jsonBody, type PluginPageContext } from '@shinano/plugin-sdk'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { BaseState, ChangeError, ChangeView, ConfigSchema, ConfigView, ConstraintsView, Plugin, ProcessState, SchemaProperty, ScopeCandidate, ScopeKind, Scopes, Status, VersionChange } from './types'

const props = defineProps<{ context: PluginPageContext }>()

const REFRESH_MS = 10_000
const LOG_LINES = 200
const STATE_LABEL: Record<ProcessState, string> = {
  Stopped: '已停止',
  Starting: '启动中',
  Running: '运行中',
  Stopping: '停止中',
  Failed: '已停止（重启次数超限）',
  Missing: '缺少运行环境'
}
const STATE_CLASS: Record<ProcessState, string> = {
  Stopped: 'badge-ghost',
  Starting: 'badge-warning',
  Running: 'badge-success',
  Stopping: 'badge-warning',
  Failed: 'badge-error',
  Missing: 'badge-error'
}
const BASE_NOTICE: Partial<Record<BaseState, string>> = {
  Incomplete: 'NB2 环境刚建立，插件层尚未安装；启动 NB2 时会自动安装，也可以现在重建。',
  Outdated: 'NB2 基础环境与当前 NB2Bridge 版本不一致（NB2Bridge 已升级），建议重建环境；重建前不能安装或升级插件。',
  Unmanaged: 'NB2 基础环境不是按锁定文件建立的（旧版建立），建议重建环境；重建前不能安装或升级插件。'
}
const SCOPE_KINDS: ScopeKind[] = ['group', 'private']
const SCOPE_LABEL: Record<ScopeKind, { title: string; noun: string }> = {
  group: { title: '群聊', noun: '群' },
  private: { title: '私聊', noun: '好友' }
}

/** 表单字段的编辑方式。 */
type FieldKind = 'text' | 'number' | 'boolean' | 'enum' | 'list' | 'json'

interface FormField {
  key: string
  title: string
  description: string
  kind: FieldKind
  options: string[]
  nullable: boolean
}

const error = ref('')
const notice = ref('')
const status = ref<Status | null>(null)
const plugins = ref<Plugin[]>([])
const logs = ref<string[]>([])
const busy = ref(false)
const installPackage = ref('')
const installVersion = ref('')
const installModule = ref('')
const wheelModule = ref('')
const wheelInput = ref<HTMLInputElement | null>(null)
const removing = ref<Plugin | null>(null)
const editing = ref<Plugin | null>(null)
const fields = ref<FormField[]>([])
const drafts = ref<Record<string, string | boolean | null>>({})
const changed = ref<Set<string>>(new Set())
const restartHint = ref(false)
const scopes = ref<Scopes | null>(null)
const scopeEditing = ref<ScopeKind | null>(null)
const scopeWhitelist = ref(false)
const scopeSelected = ref<number[]>([])
const scopeCandidates = ref<ScopeCandidate[]>([])
const scopeLoading = ref(false)
const scopeFilter = ref('')
const changes = ref<VersionChange[]>([])
/** 上一次安装失败的来源；用于「允许调整已装插件的依赖后重试」。 */
const retry = ref<'pypi' | 'wheel' | null>(null)
const rebuildConfirm = ref(false)
const constraints = ref('')
let refreshTimer: number | undefined

const running = computed(() => status.value?.state === 'Running')
const filteredCandidates = computed(() => {
  const keyword = scopeFilter.value.trim()
  return keyword
    ? scopeCandidates.value.filter((candidate) => String(candidate.id).includes(keyword) || (candidate.name ?? '').includes(keyword))
    : scopeCandidates.value
})

// 只在失败时写入错误；清除由各操作在开始时自行处理，定时刷新不清掉操作的错误。
async function call<T>(path: string, init?: RequestInit): Promise<ApiResult<T>> {
  const result = await callApi<T>(props.context, path, init)
  if (result.error) error.value = result.error
  return result
}

async function refresh() {
  const [statusResult, pluginsResult, logsResult] = await Promise.all([
    call<Status>('/status'),
    call<Plugin[]>('/plugins'),
    call<string[]>(`/logs?lines=${LOG_LINES}`)
  ])
  if (statusResult.ok) status.value = statusResult.body
  if (pluginsResult.ok) plugins.value = pluginsResult.body ?? []
  if (logsResult.ok) logs.value = logsResult.body ?? []
}

async function processAction(action: 'start' | 'stop' | 'restart') {
  error.value = ''
  busy.value = true
  const result = await call<Status>(`/process/${action}`, { method: 'POST' })
  busy.value = false
  if (result.ok) {
    status.value = result.body
    if (action !== 'stop') restartHint.value = false
  }
  await refresh()
}

async function setEnabled(plugin: Plugin) {
  error.value = ''
  const result = await call(`/plugins/${encodeURIComponent(plugin.module)}/enabled`, jsonBody('PUT', { enabled: plugin.enabled }))
  if (!result.ok) await refresh()
}

function describeChanges(result: ChangeView | null): string {
  const list = result?.changes ?? []
  changes.value = list
  return list.length > 0 ? `，插件层 ${list.length} 个包版本变化（见下方列表）` : ''
}

async function install(relaxInstalled = false) {
  error.value = ''
  notice.value = ''
  retry.value = null
  busy.value = true
  const result = await call<ChangeView>('/plugins', jsonBody('POST', { package: installPackage.value, version: installVersion.value, module: installModule.value || null, relaxInstalled }))
  busy.value = false
  if (result.ok) {
    notice.value = `已安装 ${installPackage.value} ${installVersion.value}，默认不启用${describeChanges(result.body)}`
    installPackage.value = ''
    installVersion.value = ''
    installModule.value = ''
  } else if ((result.body as ChangeError | null)?.conflict) {
    retry.value = 'pypi'
  }
  await refresh()
}

async function uploadWheel(relaxInstalled = false) {
  const file = wheelInput.value?.files?.[0]
  if (!file) return
  error.value = ''
  notice.value = ''
  retry.value = null
  busy.value = true
  const form = new FormData()
  form.append('file', file)
  if (wheelModule.value) form.append('module', wheelModule.value)
  if (relaxInstalled) form.append('relaxInstalled', 'true')
  const result = await call<ChangeView>('/plugins/wheel', { method: 'POST', body: form })
  busy.value = false
  if (result.ok) {
    notice.value = `已安装 ${file.name}，默认不启用${describeChanges(result.body)}`
    wheelModule.value = ''
    if (wheelInput.value) wheelInput.value.value = ''
  } else if ((result.body as ChangeError | null)?.conflict) {
    retry.value = 'wheel'
  }
  await refresh()
}

async function retryRelaxed() {
  const kind = retry.value
  if (kind === 'pypi') await install(true)
  else if (kind === 'wheel') await uploadWheel(true)
}

async function uninstall() {
  if (!removing.value) return
  error.value = ''
  notice.value = ''
  retry.value = null
  busy.value = true
  const result = await call<ChangeView>(`/plugins/${encodeURIComponent(removing.value.module)}`, { method: 'DELETE' })
  busy.value = false
  if (result.ok) notice.value = `已卸载 ${removing.value.package}${describeChanges(result.body)}`
  removing.value = null
  await refresh()
}

async function rebuild() {
  rebuildConfirm.value = false
  error.value = ''
  notice.value = ''
  retry.value = null
  busy.value = true
  const result = await call<ChangeView>('/environment/rebuild', { method: 'POST' })
  busy.value = false
  if (result.ok) notice.value = `NB2 环境已重建${describeChanges(result.body)}`
  await refresh()
}

async function loadConstraints() {
  const result = await call<ConstraintsView>('/constraints')
  if (result.ok) constraints.value = result.body?.text ?? ''
}

async function saveConstraints() {
  error.value = ''
  notice.value = ''
  const result = await call('/constraints', jsonBody('PUT', { text: constraints.value }))
  if (result.ok) notice.value = '约束已保存，此后的安装与重建生效'
}

// pydantic 的 Optional 字段为 anyOf [类型, null]，枚举与嵌套模型经 $ref 引用 $defs。
function resolve(schema: ConfigSchema, property: SchemaProperty): SchemaProperty {
  if (property.$ref) return schema.$defs?.[property.$ref.split('/').pop() ?? ''] ?? property
  return property
}

function toField(schema: ConfigSchema, key: string, raw: SchemaProperty): FormField {
  const branches = raw.anyOf ?? [raw]
  const nullable = branches.some((branch) => branch.type === 'null')
  const property = resolve(schema, branches.find((branch) => branch.type !== 'null') ?? raw)
  const items = property.items ? resolve(schema, property.items) : undefined
  let kind: FieldKind = 'json'
  if (property.enum) kind = 'enum'
  else if (property.type === 'boolean') kind = 'boolean'
  else if (property.type === 'integer' || property.type === 'number') kind = 'number'
  else if (property.type === 'string') kind = 'text'
  else if (property.type === 'array' && items && (items.type === 'string' || items.enum)) kind = 'list'
  return {
    key,
    title: raw.title ?? property.title ?? key,
    description: raw.description ?? property.description ?? '',
    kind,
    options: (property.enum ?? []).map(String),
    nullable
  }
}

function toDraft(field: FormField, value: unknown): string | boolean | null {
  if (value === null || value === undefined) return field.kind === 'boolean' ? false : null
  switch (field.kind) {
    case 'boolean':
      return Boolean(value)
    case 'list':
      return (value as unknown[]).map(String).join('\n')
    case 'json':
      return JSON.stringify(value, null, 2)
    default:
      return String(value)
  }
}

function fromDraft(field: FormField, draft: string | boolean | null): unknown {
  if (draft === null) return null
  switch (field.kind) {
    case 'boolean':
      return draft
    case 'number':
      // 清空时提交 null 而不是 Number('') 得到的 0：可空项由此清除，不可空项由服务端校验报错，不会悄悄写入 0。
      return draft === '' ? null : Number(draft)
    case 'list':
      return String(draft).split('\n').map((line) => line.trim()).filter((line) => line.length > 0)
    case 'json':
      return JSON.parse(String(draft))
    default:
      return draft === '' && field.nullable ? null : draft
  }
}

async function openConfig(plugin: Plugin) {
  error.value = ''
  const result = await call<ConfigView>(`/plugins/${encodeURIComponent(plugin.module)}/config`)
  if (!result.ok || !result.body) return
  const view = result.body
  fields.value = Object.entries(view.schema.properties ?? {}).map(([key, property]) => toField(view.schema, key, property))
  drafts.value = Object.fromEntries(fields.value.map((field) => [field.key, toDraft(field, view.values[field.key])]))
  changed.value = new Set()
  editing.value = plugin
}

function markChanged(key: string) {
  changed.value = new Set([...changed.value, key])
}

function setText(key: string, event: Event) {
  drafts.value[key] = (event.target as HTMLTextAreaElement).value
  markChanged(key)
}

function resetField(field: FormField) {
  drafts.value[field.key] = field.kind === 'boolean' ? false : null
  markChanged(field.key)
}

async function saveConfig() {
  if (!editing.value) return
  error.value = ''
  let values: Record<string, unknown>
  try {
    values = Object.fromEntries(
      fields.value.filter((field) => changed.value.has(field.key)).map((field) => [field.key, fromDraft(field, drafts.value[field.key] ?? null)])
    )
  } catch {
    error.value = 'JSON 格式不正确'
    return
  }

  const result = await call(`/plugins/${encodeURIComponent(editing.value.module)}/config`, jsonBody('PUT', values))
  if (!result.ok) return
  editing.value = null
  restartHint.value = true
}

function formatUnix(value: number): string {
  return formatTime(new Date(value * 1000).toISOString())
}

async function loadScopes() {
  const result = await call<Scopes>('/scope')
  if (result.ok) scopes.value = result.body
}

function scopeSummary(kind: ScopeKind): string {
  const scope = scopes.value?.[kind]
  if (!scope) return ''
  const count = scope.ids.length > 0 ? `${scope.ids.length} 个${SCOPE_LABEL[kind].noun}` : '未选择'
  return `${scope.whitelist ? '白名单' : '黑名单'} · ${count}`
}

async function openScope(kind: ScopeKind) {
  const scope = scopes.value?.[kind]
  if (!scope) return
  error.value = ''
  scopeEditing.value = kind
  scopeWhitelist.value = scope.whitelist
  scopeSelected.value = [...scope.ids]
  scopeFilter.value = ''
  scopeCandidates.value = []
  scopeLoading.value = true
  const result = await call<ScopeCandidate[]>(`/scope/${kind}/candidates`)
  scopeLoading.value = false
  if (result.ok) scopeCandidates.value = result.body ?? []
}

function toggleScopeSelected(id: number, checked: boolean) {
  scopeSelected.value = checked ? [...scopeSelected.value, id] : scopeSelected.value.filter((selected) => selected !== id)
}

async function saveScope() {
  if (!scopeEditing.value) return
  error.value = ''
  const result = await call(`/scope/${scopeEditing.value}`, jsonBody('PUT', { whitelist: scopeWhitelist.value, ids: scopeSelected.value }))
  if (!result.ok) return
  scopeEditing.value = null
  await loadScopes()
}

onMounted(async () => {
  await Promise.all([refresh(), loadScopes(), loadConstraints()])
  refreshTimer = window.setInterval(refresh, REFRESH_MS)
})
onBeforeUnmount(() => window.clearInterval(refreshTimer))
</script>

<template>
  <div class="flex flex-col gap-6">
    <div v-if="error" role="alert" class="alert alert-error flex flex-col items-start gap-2">
      <span class="[overflow-wrap:anywhere] whitespace-pre-wrap">{{ error }}</span>
      <button v-if="retry" class="btn btn-sm" :disabled="busy" @click="retryRelaxed">允许调整已装插件的依赖后重试</button>
    </div>
    <div v-if="notice" role="status" class="alert alert-success flex flex-col items-start gap-2">
      <span>{{ notice }}</span>
      <ul v-if="changes.length > 0" class="text-xs tabular-nums">
        <li v-for="change in changes" :key="change.package"><span class="font-mono">{{ change.package }}</span> {{ change.from ?? "（新增）" }} → {{ change.to ?? "（移除）" }}</li>
      </ul>
    </div>
    <div v-if="status && BASE_NOTICE[status.baseState]" role="status" class="alert alert-warning flex items-center gap-4">
      <span>{{ BASE_NOTICE[status.baseState] }}</span>
      <button class="btn btn-sm ml-auto" :disabled="busy" @click="rebuildConfirm = true">重建环境</button>
    </div>
    <div v-if="restartHint" role="status" class="alert alert-warning flex items-center gap-4">
      <span>配置已保存到 .env，重启 NB2 后生效；重启会影响 NB2 内的全部插件。</span>
      <button class="btn btn-sm ml-auto" :disabled="busy" @click="processAction('restart')"><PhArrowsClockwise class="size-4" />重启 NB2</button>
    </div>

    <section class="flex flex-col gap-3">
      <div class="flex items-center gap-2">
        <h3 class="font-semibold">进程</h3>
        <button class="btn btn-ghost btn-xs" aria-label="刷新" @click="refresh"><PhArrowsClockwise class="size-4" />刷新</button>
      </div>
      <p class="text-sm opacity-70">NB2 服务随本插件启用而启动、停用而停止；意外退出时自动重启。NB2 首次启动时宿主会按配置准备 ffmpeg（自动下载约 200 MB）。</p>
      <div v-if="status" class="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1 text-sm">
        <span class="opacity-70">状态</span>
        <span class="flex items-center gap-2">
          <span class="badge" :class="STATE_CLASS[status.state]">{{ STATE_LABEL[status.state] }}</span>
          <span v-if="status.pid" class="tabular-nums opacity-70">PID {{ status.pid }}，启动于 {{ formatTime(status.startedAt) }}</span>
        </span>
        <span class="opacity-70">事件连接</span>
        <span>{{ status.connected ? '已连接' : '未连接' }}</span>
        <span class="opacity-70">ffmpeg</span>
        <span class="[overflow-wrap:anywhere]">
          <template v-if="status.ffmpegDirectory">可用<template v-if="status.ffmpegVersion">，版本 {{ status.ffmpegVersion }}</template>：{{ status.ffmpegDirectory }}</template>
          <span v-else-if="status.ffmpegError" class="text-warning-strong">不可用：{{ status.ffmpegError }}</span>
          <template v-else>尚未检查（启动 NB2 时检查）</template>
        </span>
        <span class="opacity-70">自动重启</span>
        <span class="tabular-nums">自上次就绪起 {{ status.autoRestarts }} 次（最多 2 次）<template v-if="status.lastExitCode !== null">，上次退出码 {{ status.lastExitCode }}</template></span>
        <template v-if="status.lastError">
          <span class="opacity-70">最近错误</span>
          <span class="text-error-strong [overflow-wrap:anywhere]">{{ status.lastError }}</span>
        </template>
      </div>
      <div class="flex gap-2">
        <button class="btn btn-primary btn-sm" :disabled="busy || status?.state === 'Running' || status?.state === 'Starting'" @click="processAction('start')"><PhPlay class="size-4" />启动</button>
        <button class="btn btn-sm" :disabled="busy || !status?.pid" @click="processAction('restart')"><PhArrowsClockwise class="size-4" />重启</button>
        <button class="btn btn-sm" :disabled="busy || !status?.pid" @click="processAction('stop')"><PhStop class="size-4" />停止</button>
        <button class="btn btn-sm" :disabled="busy || status?.baseState === 'Missing'" title="按当前 NB2Bridge 的基础锁定文件重建 NB2 运行环境" @click="rebuildConfirm = true"><PhWrench class="size-4" />重建环境</button>
        <span v-if="busy" class="loading loading-spinner loading-sm self-center"></span>
      </div>
      <details v-if="status && status.environment.length > 0" class="text-sm">
        <summary class="cursor-pointer opacity-70">本次启动传入的环境变量（{{ status.environment.length }}）</summary>
        <table class="table table-xs mt-2">
          <tbody>
            <tr v-for="variable in status.environment" :key="variable.name">
              <td class="font-mono">{{ variable.name }}</td>
              <td class="font-mono [overflow-wrap:anywhere]">{{ variable.display }}</td>
            </tr>
          </tbody>
        </table>
      </details>
    </section>

    <section class="flex flex-col gap-3">
      <h3 class="font-semibold">响应范围</h3>
      <p class="text-sm opacity-70">只限制交付给 NB2 的消息与事件，保存后立即生效；好友申请、入群邀请与 NB2 主动发送不受限制。</p>
      <div v-if="scopes" class="grid grid-cols-[auto_1fr_auto] items-center gap-x-6 gap-y-2 text-sm">
        <template v-for="kind in SCOPE_KINDS" :key="kind">
          <span class="opacity-70">{{ SCOPE_LABEL[kind].title }}</span>
          <span>
            {{ scopeSummary(kind) }}
            <span v-if="scopes[kind].whitelist && scopes[kind].ids.length === 0" class="ml-2 text-warning-strong">不响应任何{{ SCOPE_LABEL[kind].title }}</span>
          </span>
          <button class="btn btn-ghost btn-xs" :aria-label="`编辑${SCOPE_LABEL[kind].title}响应范围`" @click="openScope(kind)"><PhPencilSimple class="size-4" />编辑</button>
        </template>
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <h3 class="font-semibold">插件</h3>
      <p class="text-sm opacity-70">停止只暂停该插件的事件响应并取消其等待中的连续对话，插件的定时任务与后台工作不受影响。统计在 NB2 重启后清零。</p>
      <div class="overflow-x-auto">
        <table class="table table-sm">
          <thead>
            <tr><th>插件</th><th class="w-20">响应</th><th class="text-right">matcher</th><th class="text-right">事件</th><th class="text-right">执行</th><th class="text-right">错误</th><th class="text-right">API</th><th>最近错误</th><th class="w-24"></th></tr>
          </thead>
          <tbody>
            <tr v-for="plugin in plugins" :key="plugin.module">
              <td>
                <div>{{ plugin.remote?.name ?? plugin.package }}</div>
                <div class="text-xs opacity-60">{{ plugin.package }} {{ plugin.version }}<template v-if="plugin.fromWheel">（wheel）</template> · {{ plugin.module }}</div>
                <div v-if="plugin.remote && !plugin.remote.loaded" class="text-xs text-error-strong">加载失败，详见下方日志</div>
              </td>
              <td><input v-model="plugin.enabled" type="checkbox" class="toggle toggle-sm" :aria-label="`${plugin.package} 响应事件`" @change="setEnabled(plugin)" /></td>
              <td class="text-right tabular-nums">{{ plugin.remote?.matchers ?? '—' }}</td>
              <td class="text-right tabular-nums">{{ plugin.remote?.stats.events ?? '—' }}</td>
              <td class="text-right tabular-nums">{{ plugin.remote?.stats.runs ?? '—' }}</td>
              <td class="text-right tabular-nums">{{ plugin.remote?.stats.errors ?? '—' }}</td>
              <td class="text-right tabular-nums">{{ plugin.remote?.stats.apiCalls ?? '—' }}</td>
              <td class="max-w-64 text-xs [overflow-wrap:anywhere]">
                <template v-if="plugin.remote?.stats.lastError">{{ plugin.remote.stats.lastError.type }}：{{ plugin.remote.stats.lastError.message }}<span class="block opacity-60">{{ formatUnix(plugin.remote.stats.lastError.time) }}</span></template>
              </td>
              <td class="text-right">
                <button class="btn btn-ghost btn-xs btn-square" :disabled="!running || !plugin.remote?.hasConfig" :aria-label="`配置 ${plugin.package}`" :title="!running ? 'NB2 运行时才能读取配置模型' : plugin.remote?.hasConfig ? '配置' : '无可配置项'" @click="openConfig(plugin)"><PhGear class="size-4" /></button>
                <button class="btn btn-ghost btn-xs btn-square" :disabled="busy" :aria-label="`卸载 ${plugin.package}`" @click="removing = plugin"><PhTrash class="size-4" /></button>
              </td>
            </tr>
            <tr v-if="plugins.length === 0">
              <td colspan="9" class="py-4 text-center opacity-60">没有安装 NB2 插件</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="text-sm opacity-70">安装、卸载与重建会先停止 NB2，完成后恢复原来的运行状态；装好后实际加载全部插件检查，失败时整体还原。新插件不能改变 NB2 基础环境与已装插件的依赖版本，冲突时可确认后放宽重试。只安装经人工认可来源与代码的插件。</p>
      <div class="flex flex-wrap items-end gap-2">
        <label class="flex flex-col gap-1 text-sm">PyPI 包名<input v-model="installPackage" class="input input-sm w-64" placeholder="nonebot-plugin-xxx" /></label>
        <label class="flex flex-col gap-1 text-sm">版本<input v-model="installVersion" class="input input-sm w-28" placeholder="1.0.0" /></label>
        <label class="flex flex-col gap-1 text-sm">模块名（可选）<input v-model="installModule" class="input input-sm w-56" placeholder="默认为包名把 - 换成 _" /></label>
        <button class="btn btn-primary btn-sm" :disabled="busy || !installPackage || !installVersion" @click="install()">安装</button>
      </div>
      <div class="flex flex-wrap items-end gap-2">
        <label class="flex flex-col gap-1 text-sm">wheel 文件<input ref="wheelInput" type="file" accept=".whl" class="file-input file-input-sm w-72" /></label>
        <label class="flex flex-col gap-1 text-sm">模块名（可选）<input v-model="wheelModule" class="input input-sm w-56" placeholder="默认为包名" /></label>
        <button class="btn btn-sm" :disabled="busy" @click="uploadWheel()"><PhUploadSimple class="size-4" />上传安装</button>
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <h3 class="font-semibold">依赖约束</h3>
      <p class="text-sm opacity-70">用于排除已知有问题的版本，此后安装插件与重建环境时生效；不能改变 NB2 基础环境。每行一个包名与版本条件，例如 <code>tarina&lt;0.8</code>，可用 # 写明原因。</p>
      <textarea v-model="constraints" class="textarea textarea-sm w-full max-w-2xl font-mono" rows="4" placeholder="tarina<0.8  # 0.8.0 与 arclet-alconna 1.8 不兼容" aria-label="依赖约束"></textarea>
      <div><button class="btn btn-sm" :disabled="busy" @click="saveConstraints">保存约束</button></div>
    </section>

    <section class="flex flex-col gap-3">
      <div class="flex items-center gap-2">
        <h3 class="font-semibold">日志</h3>
        <span class="text-sm opacity-70">最近 {{ LOG_LINES }} 行，完整日志在部署数据目录的 nb2/logs/</span>
      </div>
      <pre class="max-h-96 overflow-auto rounded-box bg-base-200 p-3 text-xs leading-5 whitespace-pre-wrap [overflow-wrap:anywhere]">{{ logs.length > 0 ? logs.join('\n') : '没有输出' }}</pre>
    </section>

    <Teleport to="body">
      <div v-if="rebuildConfirm" class="modal modal-open" role="dialog" aria-labelledby="nb2-rebuild-title">
        <div class="modal-box flex flex-col gap-4">
          <h3 id="nb2-rebuild-title" class="text-lg font-semibold">重建 NB2 环境</h3>
          <p class="text-sm">将停止 NB2，按当前 NB2Bridge 的基础锁定文件同步 NB2 基础环境，再重新安装全部 NB2 插件并逐一试加载，之后恢复 NB2 的运行状态。基础环境未变时按现有锁定精确还原；基础环境变化时先保留插件原有的依赖版本，不行再重新解析，版本变化会列在结果中。失败时整体还原。</p>
          <div class="flex justify-end gap-2">
            <button class="btn btn-ghost btn-sm" :disabled="busy" @click="rebuildConfirm = false">取消</button>
            <button class="btn btn-primary btn-sm" :disabled="busy" @click="rebuild"><span v-if="busy" class="loading loading-spinner loading-xs"></span>重建</button>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="removing" class="modal modal-open" role="dialog" aria-labelledby="nb2-remove-title">
        <div class="modal-box flex flex-col gap-4">
          <h3 id="nb2-remove-title" class="text-lg font-semibold">卸载 {{ removing.package }}</h3>
          <p class="text-sm">将停止 NB2、卸载该包并更新锁定文件，之后恢复 NB2 的运行状态。其他插件可能共用的依赖不会删除，插件自己保存的数据也不会删除。</p>
          <div class="flex justify-end gap-2">
            <button class="btn btn-ghost btn-sm" :disabled="busy" @click="removing = null">取消</button>
            <button class="btn btn-error btn-sm" :disabled="busy" @click="uninstall"><span v-if="busy" class="loading loading-spinner loading-xs"></span>卸载</button>
          </div>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="editing" class="modal modal-open" role="dialog" aria-labelledby="nb2-config-title">
        <div class="modal-box flex max-h-[85vh] w-11/12 max-w-2xl flex-col gap-3">
          <header class="flex items-center">
            <h3 id="nb2-config-title" class="text-lg font-semibold">配置 {{ editing.remote?.name ?? editing.package }}</h3>
            <button class="btn btn-ghost btn-sm btn-square ml-auto" aria-label="关闭" @click="editing = null"><PhX class="size-4" /></button>
          </header>
          <p class="text-sm opacity-70">显示当前生效的值；只保存修改过的项，写入 .env 后需要重启 NB2 生效。「恢复默认」删除 .env 中的该项。</p>
          <div class="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto pr-1">
            <div v-for="field in fields" :key="field.key" class="flex flex-col gap-1">
              <div class="flex items-center gap-2">
                <span class="text-sm font-medium">{{ field.title }}</span>
                <span class="font-mono text-xs opacity-50">{{ field.key.toUpperCase() }}</span>
                <span v-if="changed.has(field.key)" class="badge badge-xs badge-info">已修改</span>
                <button class="btn btn-ghost btn-xs ml-auto" @click="resetField(field)">恢复默认</button>
              </div>
              <p v-if="field.description" class="text-xs opacity-70">{{ field.description }}</p>
              <input v-if="field.kind === 'boolean'" v-model="drafts[field.key]" type="checkbox" class="toggle toggle-sm" :aria-label="field.title" @change="markChanged(field.key)" />
              <select v-else-if="field.kind === 'enum'" v-model="drafts[field.key]" class="select select-sm w-full" :aria-label="field.title" @change="markChanged(field.key)">
                <option v-if="field.nullable" :value="null">（未设置）</option>
                <option v-for="option in field.options" :key="option" :value="option">{{ option }}</option>
              </select>
              <input v-else-if="field.kind === 'number'" v-model="drafts[field.key]" type="number" class="input input-sm w-full" :aria-label="field.title" @input="markChanged(field.key)" />
              <input v-else-if="field.kind === 'text'" v-model="drafts[field.key]" class="input input-sm w-full" :aria-label="field.title" @input="markChanged(field.key)" />
              <textarea v-else :value="String(drafts[field.key] ?? '')" class="textarea textarea-sm w-full font-mono" :rows="field.kind === 'list' ? 3 : 4" :placeholder="field.kind === 'list' ? '每行一项' : 'JSON'" :aria-label="field.title" @input="setText(field.key, $event)"></textarea>
            </div>
          </div>
          <footer class="flex justify-end gap-2">
            <button class="btn btn-ghost btn-sm" @click="editing = null">取消</button>
            <button class="btn btn-primary btn-sm" :disabled="changed.size === 0" @click="saveConfig">保存</button>
          </footer>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div v-if="scopeEditing" class="modal modal-open" role="dialog" aria-labelledby="nb2-scope-title">
        <div class="modal-box flex max-h-[80vh] w-11/12 max-w-md flex-col gap-3">
          <header class="flex items-center">
            <h3 id="nb2-scope-title" class="text-lg font-semibold">{{ SCOPE_LABEL[scopeEditing].title }}响应范围</h3>
            <button class="btn btn-ghost btn-sm btn-square ml-auto" aria-label="关闭" @click="scopeEditing = null"><PhX class="size-4" /></button>
          </header>
          <div class="flex items-center gap-2">
            <div class="join" role="group" aria-label="名单模式">
              <button class="btn join-item btn-sm" :class="{ 'btn-active': scopeWhitelist }" :aria-pressed="scopeWhitelist" @click="scopeWhitelist = true">白名单</button>
              <button class="btn join-item btn-sm" :class="{ 'btn-active': !scopeWhitelist }" :aria-pressed="!scopeWhitelist" @click="scopeWhitelist = false">黑名单</button>
            </div>
            <button class="btn btn-ghost btn-sm ml-auto" :disabled="scopeSelected.length === 0" @click="scopeSelected = []">清空选择</button>
          </div>
          <p class="text-xs opacity-70">{{ scopeWhitelist ? `只响应勾选的${SCOPE_LABEL[scopeEditing].title}，不勾选则全部不响应` : `不响应勾选的${SCOPE_LABEL[scopeEditing].title}，其余照常响应` }}</p>
          <input v-model="scopeFilter" class="input input-sm w-full" placeholder="按名称或号码筛选" aria-label="筛选" />
          <ul class="min-h-0 flex-1 overflow-y-auto">
            <li v-for="candidate in filteredCandidates" :key="candidate.id">
              <label class="flex cursor-pointer items-center gap-3 rounded-box px-2 py-1.5 hover:bg-base-200">
                <input type="checkbox" class="checkbox checkbox-sm" :checked="scopeSelected.includes(candidate.id)" @change="toggleScopeSelected(candidate.id, ($event.target as HTMLInputElement).checked)" />
                <span class="[overflow-wrap:anywhere]">{{ candidate.name ?? (scopeEditing === 'group' ? '（Bot 已不在该群）' : '（已不是好友）') }}<span class="ml-1 text-xs opacity-60 tabular-nums">{{ candidate.id }}</span></span>
              </label>
            </li>
            <li v-if="scopeLoading" class="py-4 text-center"><span class="loading loading-spinner loading-sm"></span></li>
            <li v-else-if="filteredCandidates.length === 0" class="py-4 text-center text-sm opacity-60">{{ scopeCandidates.length === 0 ? `没有${SCOPE_LABEL[scopeEditing].noun}（Bot 未连接）` : '没有匹配的项' }}</li>
          </ul>
          <footer class="flex items-center gap-2">
            <span class="text-sm opacity-70 tabular-nums">已选 {{ scopeSelected.length }} 个{{ SCOPE_LABEL[scopeEditing].noun }}</span>
            <button class="btn btn-ghost btn-sm ml-auto" @click="scopeEditing = null">取消</button>
            <button class="btn btn-primary btn-sm" :disabled="scopeLoading" @click="saveScope">保存</button>
          </footer>
        </div>
      </div>
    </Teleport>
  </div>
</template>
