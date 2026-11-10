<script setup lang="ts">
import { PhArrowClockwise, PhArrowCounterClockwise, PhTrash, PhUploadSimple } from '@phosphor-icons/vue'
import { computed, onMounted, onUnmounted, ref, watch } from 'vue'
import { api, describeError, request } from '../api'
import PageToolbar from '../components/PageToolbar.vue'
import { formatBytes, formatTime } from '../format'
import { plugins, refreshPlugins } from '../plugins'
import { changeBadge, changeLabel, describeChange, describeRollback, openRestartDialog, refreshRestart, restartStatus } from '../restart'
import type {
  PendingPluginChange,
  PluginDataPreview,
  PluginPurge,
  PluginPurgeState,
  PluginPurgeStepKind,
  PluginSnapshot,
  RestartOutcome
} from '../types'

const error = ref('')
const notice = ref('')
const busy = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const removeDialog = ref<HTMLDialogElement | null>(null)
const purgeDialog = ref<HTMLDialogElement | null>(null)
const removing = ref<PluginSnapshot | null>(null)
const purgeData = ref(false)
const preview = ref<PluginDataPreview | null>(null)
const purges = ref<PluginPurge[]>([])
let purgeTimer: number | undefined

const PURGE_POLL_MS = 3000

const PURGE_STATE: Record<PluginPurgeState, { cls: string; title: string }> = {
  Waiting: { cls: 'alert-info', title: '等待执行（数据库与 RustFS 可用后开始）' },
  Running: { cls: 'alert-info', title: '进行中' },
  Completed: { cls: 'alert-success', title: '已完成' },
  PartiallyFailed: { cls: 'alert-error', title: '部分失败' },
  Cancelled: { cls: 'alert-warning', title: '已放弃：同 ID 的插件已重新安装，剩余步骤不再执行' }
}

const STEP_LABEL: Record<PluginPurgeStepKind, string> = {
  Tables: '数据表',
  SchemaVersions: '结构版本',
  MediaReferences: '持久媒体',
  Buckets: 'bucket',
  ConfigAndState: '配置与状态',
  Secrets: '机密',
  BrowserProfile: '浏览器目录',
  Leftovers: '可能的残留'
}

const pending = computed(() => restartStatus.value?.pending ?? [])
const pendingById = computed(() => new Map(pending.value.map((item) => [item.change.pluginId, item])))
const lastResult = computed(() => restartStatus.value?.lastResult ?? null)

const OUTCOME: Record<RestartOutcome, { cls: string; title: string }> = {
  Succeeded: { cls: 'alert-success', title: '插件包变更已生效' },
  RolledBack: { cls: 'alert-warning', title: '插件包变更已回滚，已还原上一套插件包' },
  Unverified: { cls: 'alert-info', title: '插件包已应用，但基础设施处于故障模式，尚未经加载验证' }
}

onMounted(() => run(() => Promise.all([refreshPlugins(), refreshRestart(), refreshPurges()]).then(() => undefined)))
onUnmounted(() => window.clearTimeout(purgeTimer))
// 清除在重启后由新进程执行，连上新进程时重新读取。
watch(
  () => restartStatus.value?.instanceId,
  () => void refreshPurges().catch(() => undefined)
)

// 等待或执行中的清除定时刷新，直到结束。
async function refreshPurges(): Promise<void> {
  window.clearTimeout(purgeTimer)
  purges.value = await request<PluginPurge[]>('/api/plugin-purges')
  if (purges.value.some((purge) => purge.state === 'Waiting' || purge.state === 'Running'))
    purgeTimer = window.setTimeout(() => void refreshPurges().catch(() => undefined), PURGE_POLL_MS)
}

function purgeClass(purge: PluginPurge): string {
  return purge.state === 'Completed' && purge.possibleLeftovers.length > 0 ? 'alert-warning' : PURGE_STATE[purge.state].cls
}

function dismissPurge(pluginId: string): Promise<void> {
  return run(async () => {
    await api.delete(`/api/plugin-purges/${pluginId}`)
    await refreshPurges()
  })
}

async function run(action: () => Promise<void>): Promise<void> {
  busy.value = true
  error.value = ''
  try {
    await action()
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

function toggle(plugin: PluginSnapshot): Promise<void> {
  return run(async () => {
    await api.post(`/api/plugins/${plugin.id}/${plugin.enabled ? 'disable' : 'enable'}`)
    await refreshPlugins()
  })
}

// 插件顺序同时决定跨插件指令优先级与普通事件链顺序，从下一条消息生效。
function move(index: number, offset: number): Promise<void> {
  return run(async () => {
    const ids = plugins.value.map((plugin) => plugin.id)
    const [moved] = ids.splice(index, 1)
    if (moved === undefined) return
    ids.splice(index + offset, 0, moved)
    await api.put('/api/plugins/order', ids)
    await refreshPlugins()
  })
}

function pendingLabel(pluginId: string): string | null {
  const item = pendingById.value.get(pluginId)
  return item ? `待${changeLabel(item.change)}` : null
}

function commandCount(plugin: PluginSnapshot): number {
  return plugin.commandTypes.reduce((sum, type) => sum + type.commandCount, 0)
}

function upload(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return Promise.resolve()
  const body = new FormData()
  body.append('file', file)
  notice.value = ''
  return run(async () => {
    try {
      const staged = await request<PendingPluginChange>('/api/packages', { method: 'POST', body })
      notice.value = `已暂存 ${staged.change.name}（${changeLabel(staged.change)} ${describeChange(staged.change)}），重启后生效`
      await refreshRestart()
    } finally {
      input.value = ''
    }
  })
}

function undo(pluginId: string): Promise<void> {
  return run(async () => {
    await api.delete(`/api/packages/${pluginId}`)
    await refreshRestart()
  })
}

function confirmRemove(plugin: PluginSnapshot): void {
  removing.value = plugin
  purgeData.value = false
  removeDialog.value?.showModal()
}

// 选择清除数据时先列出将删除的内容，经第二个对话框确认后才暂存。
function remove(): Promise<void> {
  const plugin = removing.value
  removeDialog.value?.close()
  if (!plugin) return Promise.resolve()
  if (!purgeData.value) return stageRemoval(plugin, false)
  return run(async () => {
    preview.value = await request<PluginDataPreview>(`/api/packages/${plugin.id}/data`)
    purgeDialog.value?.showModal()
  })
}

function confirmPurge(): Promise<void> {
  purgeDialog.value?.close()
  return removing.value ? stageRemoval(removing.value, true) : Promise.resolve()
}

function stageRemoval(plugin: PluginSnapshot, purge: boolean): Promise<void> {
  return run(async () => {
    await api.post(`/api/packages/${plugin.id}/remove`, { purgeData: purge })
    await refreshRestart()
  })
}

function dismissResult(): Promise<void> {
  return run(async () => {
    await api.delete('/api/restart/result')
    await refreshRestart()
  })
}
</script>

<template>
  <PageToolbar>
    <input ref="fileInput" type="file" accept=".zip,application/zip" class="hidden" @change="upload" />
    <button class="btn btn-sm surface" :disabled="busy" @click="fileInput?.click()">
      <PhUploadSimple class="size-4" aria-hidden="true" />上传插件包
    </button>
    <button class="btn btn-sm surface" :disabled="busy" @click="openRestartDialog">
      <PhArrowClockwise class="size-4" aria-hidden="true" />重启{{ pending.length > 0 ? `并应用 ${pending.length} 项变更` : '' }}
    </button>
  </PageToolbar>

  <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
  <div v-if="notice" role="status" class="alert alert-success">{{ notice }}</div>

  <div v-if="lastResult" role="status" class="alert flex items-center gap-4" :class="OUTCOME[lastResult.outcome].cls">
    <div class="flex min-w-0 flex-1 flex-col gap-1">
      <span class="font-semibold">{{ OUTCOME[lastResult.outcome].title }}</span>
      <span class="text-sm">{{ formatTime(lastResult.finishedAt) }}</span>
      <span v-if="lastResult.reason" class="text-sm [overflow-wrap:anywhere]">原因：{{ lastResult.reason }}</span>
      <span v-for="change in lastResult.changes" :key="change.pluginId" class="text-sm">
        {{ changeLabel(change) }} {{ change.name }} {{ lastResult.outcome === 'RolledBack' ? describeRollback(change) : describeChange(change) }}
      </span>
    </div>
    <button class="btn btn-sm shrink-0" :disabled="busy" @click="dismissResult">知道了</button>
  </div>

  <div v-for="purge in purges" :key="purge.pluginId" role="status" class="alert flex items-center gap-4" :class="purgeClass(purge)">
    <div class="flex min-w-0 flex-1 flex-col gap-1">
      <span class="font-semibold">插件数据清除：{{ purge.name }}（{{ purge.pluginId }}）</span>
      <span class="text-sm">{{ formatTime(purge.finishedAt ?? purge.createdAt) }} · {{ PURGE_STATE[purge.state].title }}</span>
      <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5 text-sm">
        <template v-for="step in purge.steps" :key="step.kind">
          <dt class="opacity-70">{{ STEP_LABEL[step.kind] }}</dt>
          <dd class="[overflow-wrap:anywhere]">
            <template v-if="step.done">{{ step.summary }}</template>
            <template v-else-if="step.error">失败：{{ step.error }}，详见日志，下次启动重试</template>
            <template v-else>—</template>
            <span v-for="table in step.kind === 'Leftovers' ? purge.possibleLeftovers : []" :key="table" class="block">
              {{ table }} 以插件前缀开头但未声明，未删除
            </span>
          </dd>
        </template>
      </dl>
    </div>
    <button
      v-if="purge.state !== 'Waiting' && purge.state !== 'Running'"
      class="btn btn-sm shrink-0"
      :disabled="busy"
      @click="dismissPurge(purge.pluginId)"
    >
      知道了
    </button>
  </div>

  <section v-if="pending.length > 0" class="surface card">
    <div class="card-body">
      <h2 class="card-title text-lg">待处理变更</h2>
      <p class="text-sm opacity-70">以下变更在面板重启后应用；普通启动不会应用。</p>
      <table class="table table-sm">
        <thead>
          <tr><th class="w-20">类型</th><th>插件</th><th>版本</th><th class="w-44">暂存时间</th><th class="w-24 text-right">大小</th><th class="w-20"></th></tr>
        </thead>
        <tbody>
          <tr v-for="item in pending" :key="item.change.pluginId">
            <td><span class="badge badge-sm whitespace-nowrap" :class="changeBadge(item.change)">{{ changeLabel(item.change) }}</span></td>
            <td>{{ item.change.name }} <span class="ml-1 text-xs opacity-60">{{ item.change.pluginId }}</span></td>
            <td class="tabular-nums">{{ describeChange(item.change) }}</td>
            <td class="opacity-70">{{ formatTime(item.stagedAt) }}</td>
            <td class="text-right tabular-nums">{{ item.sizeBytes > 0 ? formatBytes(item.sizeBytes) : '—' }}</td>
            <td class="text-right">
              <button class="btn btn-ghost btn-xs whitespace-nowrap" :disabled="busy" :aria-label="`撤销 ${item.change.name} 的变更`" @click="undo(item.change.pluginId)">
                <PhArrowCounterClockwise class="size-4" aria-hidden="true" />撤销
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <section class="surface card flex-1">
    <div class="card-body">
      <p class="text-sm opacity-70">顺序靠前的插件先处理指令与事件；启停与排序从下一条消息生效。</p>
      <table class="table">
        <thead>
          <tr><th class="w-24">顺序</th><th>插件</th><th class="w-48">指令</th><th class="w-24">启用</th><th class="w-16"></th></tr>
        </thead>
        <tbody>
          <tr v-for="(plugin, index) in plugins" :key="plugin.id">
            <td>
              <div class="join">
                <button class="btn join-item btn-xs" :disabled="busy || index === 0" @click="move(index, -1)">↑</button>
                <button class="btn join-item btn-xs" :disabled="busy || index === plugins.length - 1" @click="move(index, 1)">↓</button>
              </div>
            </td>
            <td>
              <div class="font-semibold">
                <RouterLink :to="`/plugins/${plugin.id}`" class="link">{{ plugin.name || plugin.id }}</RouterLink>
                <span class="ml-2 text-sm opacity-60">{{ plugin.id }} {{ plugin.version }}</span>
                <span v-if="pendingLabel(plugin.id)" class="badge badge-warning badge-sm ml-2">{{ pendingLabel(plugin.id) }}</span>
              </div>
              <div class="text-sm opacity-70">{{ plugin.description }}</div>
              <div v-if="plugin.error" class="text-error-strong text-sm">{{ plugin.error }}</div>
            </td>
            <td>
              <span v-if="plugin.commandTypes.length === 0" class="text-sm opacity-60">首次启用后显示</span>
              <div v-else class="text-sm">
                共 {{ commandCount(plugin) }} 条
                <div v-for="type in plugin.commandTypes" :key="type.name" class="opacity-70">
                  {{ type.name }}：{{ type.commandCount }}<span v-if="!type.enabled">（未生效）</span>
                </div>
              </div>
            </td>
            <td>
              <input type="checkbox" class="toggle toggle-success" :checked="plugin.enabled" :disabled="busy || plugin.error !== null" @change="toggle(plugin)" />
            </td>
            <td class="text-right">
              <button
                class="btn btn-ghost btn-xs"
                :disabled="busy || pendingById.get(plugin.id)?.change.kind === 'Remove'"
                :aria-label="`删除插件包 ${plugin.name || plugin.id}`"
                @click="confirmRemove(plugin)"
              >
                <PhTrash class="size-4" aria-hidden="true" />
              </button>
            </td>
          </tr>
          <tr v-if="plugins.length === 0">
            <td colspan="5" class="text-center opacity-60">没有已安装的插件</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>

  <dialog ref="removeDialog" class="modal">
    <div class="modal-box">
      <h3 class="text-lg font-bold">删除插件包 {{ removing?.name || removing?.id }}？</h3>
      <p class="py-3 text-sm">删除在重启后生效。不清除数据时只移除程序包；插件的数据库表、配置、启用状态与媒体都会保留，重新安装同 ID 的插件后继续使用。</p>
      <label class="flex cursor-pointer items-center gap-2 text-sm">
        <input v-model="purgeData" type="checkbox" class="checkbox checkbox-sm checkbox-error" />
        同时清除插件数据（不可恢复，下一步列出将删除的内容）
      </label>
      <div class="modal-action">
        <button class="btn" @click="removeDialog?.close()">取消</button>
        <button class="btn btn-warning" @click="remove">{{ purgeData ? '下一步' : '删除' }}</button>
      </div>
    </div>
  </dialog>

  <dialog ref="purgeDialog" class="modal">
    <div class="modal-box">
      <h3 class="text-lg font-bold">删除 {{ removing?.name || removing?.id }} 并清除其全部数据？</h3>
      <p class="py-2 text-sm">重启确认成功后执行，清除后无法恢复。重启前可在待处理变更中撤销。将删除：</p>
      <ul v-if="preview" class="list-disc space-y-1 pl-5 text-sm [overflow-wrap:anywhere]">
        <li>
          全局数据表：{{ preview.tables.global.length > 0 ? preview.tables.global.join('、') : '无' }}
        </li>
        <li>
          每个机器人账号的数据表：{{ preview.tables.account.length > 0 ? preview.tables.account.join('、') : '无' }}
        </li>
        <li>bucket（清空后删除）：{{ preview.buckets.length > 0 ? preview.buckets.join('、') : '无' }}</li>
        <li>持久媒体引用（不再被引用的媒体经宽限期后回收）</li>
        <li>配置、启用状态与顺序</li>
        <li>机密文件：{{ preview.secretsFile ? '有' : '无' }}</li>
        <li>浏览器用户目录：{{ preview.browserProfile ? '有' : '无' }}</li>
      </ul>
      <div class="modal-action">
        <button class="btn" @click="purgeDialog?.close()">取消</button>
        <button class="btn btn-error" @click="confirmPurge">删除并清除数据</button>
      </div>
    </div>
  </dialog>
</template>
