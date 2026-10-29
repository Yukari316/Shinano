<script setup lang="ts">
import { PhArrowClockwise, PhArrowCounterClockwise, PhTrash, PhUploadSimple } from '@phosphor-icons/vue'
import { computed, onMounted, ref } from 'vue'
import { api, describeError, request } from '../api'
import PageToolbar from '../components/PageToolbar.vue'
import { formatBytes, formatTime } from '../format'
import { plugins, refreshPlugins } from '../plugins'
import { CHANGE_KIND_LABEL, describeChange, describeRollback, openRestartDialog, refreshRestart, restartStatus } from '../restart'
import type { PendingPluginChange, PluginSnapshot, RestartOutcome } from '../types'

const error = ref('')
const notice = ref('')
const busy = ref(false)
const fileInput = ref<HTMLInputElement | null>(null)
const removeDialog = ref<HTMLDialogElement | null>(null)
const removing = ref<PluginSnapshot | null>(null)

const pending = computed(() => restartStatus.value?.pending ?? [])
const pendingById = computed(() => new Map(pending.value.map((item) => [item.change.pluginId, item])))
const lastResult = computed(() => restartStatus.value?.lastResult ?? null)

const OUTCOME: Record<RestartOutcome, { cls: string; title: string }> = {
  Succeeded: { cls: 'alert-success', title: '插件包变更已生效' },
  RolledBack: { cls: 'alert-warning', title: '插件包变更已回滚，已还原上一套插件包' },
  Unverified: { cls: 'alert-info', title: '插件包已应用，但基础设施处于故障模式，尚未经加载验证' }
}

onMounted(() => run(() => Promise.all([refreshPlugins(), refreshRestart()]).then(() => undefined)))

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
  return item ? `待${CHANGE_KIND_LABEL[item.change.kind]}` : null
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
      notice.value = `已暂存 ${staged.change.name}（${CHANGE_KIND_LABEL[staged.change.kind]} ${describeChange(staged.change)}），重启后生效`
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
  removeDialog.value?.showModal()
}

function remove(): Promise<void> {
  const plugin = removing.value
  removeDialog.value?.close()
  if (!plugin) return Promise.resolve()
  return run(async () => {
    await api.post(`/api/packages/${plugin.id}/remove`)
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

  <div v-if="lastResult" role="status" class="alert" :class="OUTCOME[lastResult.outcome].cls">
    <div class="flex min-w-0 flex-col gap-1">
      <span class="font-semibold">{{ OUTCOME[lastResult.outcome].title }}</span>
      <span class="text-sm">{{ formatTime(lastResult.finishedAt) }}</span>
      <span v-if="lastResult.reason" class="text-sm [overflow-wrap:anywhere]">原因：{{ lastResult.reason }}</span>
      <span v-for="change in lastResult.changes" :key="change.pluginId" class="text-sm">
        {{ CHANGE_KIND_LABEL[change.kind] }} {{ change.name }} {{ lastResult.outcome === 'RolledBack' ? describeRollback(change) : describeChange(change) }}
      </span>
    </div>
    <button class="btn btn-sm shrink-0" :disabled="busy" @click="dismissResult">知道了</button>
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
            <td><span class="badge badge-sm" :class="item.change.kind === 'Remove' ? 'badge-warning' : 'badge-info'">{{ CHANGE_KIND_LABEL[item.change.kind] }}</span></td>
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
                <RouterLink v-if="plugin.hasConfig || plugin.pageTitle" :to="`/plugins/${plugin.id}`" class="link">{{ plugin.name || plugin.id }}</RouterLink>
                <span v-else>{{ plugin.name || plugin.id }}</span>
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
      <p class="py-3 text-sm">删除在重启后生效，只移除程序包；插件的数据库表、配置、启用状态与媒体都会保留，重新安装同 ID 的插件后继续使用。</p>
      <div class="modal-action">
        <button class="btn" @click="removeDialog?.close()">取消</button>
        <button class="btn btn-warning" @click="remove">删除</button>
      </div>
    </div>
  </dialog>
</template>
