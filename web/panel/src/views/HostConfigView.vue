<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { api, describeError } from '../api'
import PageFooter from '../components/PageFooter.vue'
import { openRestartDialog } from '../restart'
import type { HostConfigEntry } from '../types'

const SECTION_TITLES: Record<string, string> = {
  Deployment: '部署',
  Database: '数据库',
  LlBot: 'LLBot',
  RustFs: 'RustFS',
  Media: '媒体',
  ChatArchive: '聊天记录',
  Logging: '日志',
  Web: '管理面板',
  Plugins: '插件'
}

const router = useRouter()
const entries = ref<HostConfigEntry[]>([])
// 编辑中的值：整数列表以换行分隔的文本编辑，保存时再转换。
const drafts = ref<Record<string, string | number | boolean | null>>({})
const error = ref('')
const saved = ref(false)
const newToken = ref<string | null>(null)
const confirmDialog = ref<HTMLDialogElement | null>(null)

const sections = computed(() => {
  const groups = new Map<string, HostConfigEntry[]>()
  for (const entry of entries.value) {
    const group = groups.get(entry.field.section) ?? []
    group.push(entry)
    groups.set(entry.field.section, group)
  }
  return [...groups.entries()]
})

function keyOf(entry: HostConfigEntry): string {
  return `${entry.field.section}:${entry.field.key}`
}

function draftOf(entry: HostConfigEntry): string | number | boolean | null {
  return Array.isArray(entry.value) ? entry.value.join('\n') : entry.value
}

onMounted(load)

async function load(): Promise<void> {
  try {
    entries.value = await api.get<HostConfigEntry[]>('/api/config')
    drafts.value = Object.fromEntries(
      entries.value
        .filter((entry) => entry.field.kind !== 'Secret')
        .map((entry) => [keyOf(entry), draftOf(entry)])
    )
  } catch (e) {
    error.value = describeError(e)
  }
}

// 只提交修改过的配置项；未提交的项在本地配置文件中保持不变。
function changes(): Record<string, Record<string, unknown>> {
  const result: Record<string, Record<string, unknown>> = {}
  for (const entry of entries.value) {
    if (entry.field.kind === 'Secret') continue
    let value: unknown = drafts.value[keyOf(entry)]
    if (entry.field.kind === 'IntegerList') {
      value = String(value ?? '')
        .split(/[\s,]+/)
        .filter((item) => item.length > 0)
        .map(Number)
    }
    if (JSON.stringify(value) === JSON.stringify(entry.value)) continue
    result[entry.field.section] = { ...result[entry.field.section], [entry.field.key]: value }
  }
  return result
}

const pendingCount = computed(() =>
  Object.values(changes()).reduce((sum, section) => sum + Object.keys(section).length, 0)
)

async function save(): Promise<void> {
  error.value = ''
  saved.value = false
  try {
    await api.put('/api/config', changes())
    saved.value = true
    await load()
  } catch (e) {
    error.value = describeError(e)
  }
}

async function regenerateToken(): Promise<void> {
  confirmDialog.value?.close()
  try {
    const result = await api.post<{ token: string }>('/api/auth/token')
    newToken.value = result.token
  } catch (e) {
    error.value = describeError(e)
  }
}

async function finishTokenChange(): Promise<void> {
  newToken.value = null
  await router.replace({ name: 'login' })
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <form id="host-config-form" class="flex flex-col gap-4" @submit.prevent="save">
      <section v-for="[section, items] in sections" :key="section" class="surface card">
        <div class="card-body">
          <h2 class="card-title">{{ SECTION_TITLES[section] ?? section }}</h2>
          <div class="grid gap-x-8 xl:grid-cols-2">
            <fieldset v-for="entry in items" :key="keyOf(entry)" class="fieldset content-start">
              <legend class="fieldset-legend">{{ entry.field.label }}</legend>
              <span v-if="entry.field.kind === 'Secret'" class="badge" :class="entry.value ? 'badge-success' : 'badge-warning'">
                {{ entry.value ? '已配置' : '未配置' }}
              </span>
              <select v-else-if="entry.field.kind === 'Choice'" v-model="drafts[keyOf(entry)]" class="select w-full max-w-md">
                <option v-for="option in entry.field.options" :key="option" :value="option">{{ option }}</option>
              </select>
              <input
                v-else-if="entry.field.kind === 'Integer'"
                v-model.number="drafts[keyOf(entry)]"
                type="number"
                class="input w-full max-w-md"
                :min="entry.field.minimum ?? undefined"
                :max="entry.field.maximum ?? undefined"
              />
              <textarea
                v-else-if="entry.field.kind === 'IntegerList'"
                :value="String(drafts[keyOf(entry)] ?? '')"
                @input="drafts[keyOf(entry)] = ($event.target as HTMLTextAreaElement).value"
                class="textarea w-full max-w-md font-mono"
                rows="3"
                placeholder="每行一个"
              />
              <input v-else-if="entry.field.kind === 'Boolean'" v-model="drafts[keyOf(entry)]" type="checkbox" class="toggle toggle-primary" />
              <input v-else v-model="drafts[keyOf(entry)]" type="text" class="input w-full max-w-md" />
              <p v-if="entry.field.hint" class="label whitespace-normal">{{ entry.field.hint }}</p>
            </fieldset>
          </div>
        </div>
      </section>
    </form>

    <PageFooter>
      <p class="min-w-0 flex-1 text-sm">
        <span v-if="error" role="alert" class="text-error-strong">{{ error }}</span>
        <span v-else-if="pendingCount > 0">{{ pendingCount }} 项未保存的修改</span>
        <span v-else-if="saved" role="status" class="text-success-strong">已保存到本地配置文件，重启后生效</span>
        <span v-else class="opacity-70">宿主配置只在启动时读取，保存后需要重启生效；敏感配置只能在本地配置文件中修改。</span>
      </p>
      <button v-if="saved && pendingCount === 0" type="button" class="btn btn-warning" @click="openRestartDialog">立即重启</button>
      <button type="submit" form="host-config-form" class="btn btn-primary" :disabled="pendingCount === 0">保存</button>
    </PageFooter>

    <section class="surface card">
      <div class="card-body">
        <h2 class="card-title">面板 Token</h2>
        <p class="text-sm opacity-70">重新生成后，新 Token 只显示一次，全部已登录的会话（包括当前会话）立即失效。</p>
        <div class="card-actions justify-end">
          <button class="btn btn-warning" @click="confirmDialog?.showModal()">重新生成 Token</button>
        </div>
      </div>
    </section>

    <dialog ref="confirmDialog" class="modal">
      <div class="modal-box">
        <h3 class="text-lg font-bold">确认重新生成 Token？</h3>
        <p class="py-4">全部已登录的会话都会失效，需要使用新 Token 重新登录。</p>
        <div class="modal-action">
          <button class="btn" @click="confirmDialog?.close()">取消</button>
          <button class="btn btn-warning" @click="regenerateToken">重新生成</button>
        </div>
      </div>
    </dialog>

    <dialog class="modal" :open="newToken !== null">
      <div class="modal-box">
        <h3 class="text-lg font-bold">新 Token</h3>
        <p class="py-2">请立即保存，关闭后不会再次显示。</p>
        <pre class="whitespace-pre-wrap break-all rounded-box bg-[var(--raise)] p-3 font-mono">{{ newToken }}</pre>
        <div class="modal-action">
          <button class="btn btn-primary" @click="finishTokenChange">已保存，重新登录</button>
        </div>
      </div>
    </dialog>
  </div>
</template>
