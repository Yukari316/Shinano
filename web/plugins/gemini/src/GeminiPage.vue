<script setup lang="ts">
import { PhArrowsClockwise, PhPencilSimple, PhX } from '@phosphor-icons/vue'
import { type ApiResult, callApi, jsonBody, type PluginPageContext } from '@shinano/plugin-sdk'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import type { ModelOption, ModelSettings, ScopeCandidate, ScopeKind, Scopes, UsageSummary } from './types'

const props = defineProps<{ context: PluginPageContext }>()

const USAGE_RANGES: { label: string; days: number }[] = [
  { label: '今日', days: 1 },
  { label: '近 7 天', days: 7 },
  { label: '近 30 天', days: 30 },
  { label: '全部', days: 0 }
]
const SCOPE_KINDS: ScopeKind[] = ['group', 'private']
const SCOPE_LABEL: Record<ScopeKind, { title: string; noun: string }> = {
  group: { title: '群聊', noun: '群' },
  private: { title: '私聊', noun: '好友' }
}

const EFFORT_LABELS: Record<string, string> = {
  default: '默认（由模型决定）',
  minimal: 'Minimal（极低）',
  low: 'Low（低）',
  medium: 'Medium（中）',
  high: 'High（高）',
  xhigh: 'XHigh（极高）'
}

const SYSTEM_PROMPT_MAX_LENGTH = 8000
const error = ref('')
const notice = ref('')
const usageDays = ref(30)
const usage = ref<UsageSummary | null>(null)
const usageLoading = ref(false)
const usageDetailModal = ref<'model' | 'daily' | null>(null)
const modelSettings = ref<ModelSettings | null>(null)
const selectedModel = ref('gemini-4-hydrogen')
const selectedEffort = ref('medium')
const modelFilter = ref('')
const models = ref<ModelOption[]>([])
const modelsLoading = ref(false)
const modelSaving = ref(false)
const promptModalOpen = ref(false)
const promptDraft = ref('')
const promptSaving = ref(false)

const scopes = ref<Scopes | null>(null)
const scopeEditing = ref<ScopeKind | null>(null)
const scopeSelected = ref<number[]>([])
const scopeCandidates = ref<ScopeCandidate[]>([])
const scopeLoading = ref(false)
const scopeFilter = ref('')

const matchedModel = computed(() =>
  models.value.find((item) => item.id.toLowerCase() === normalizeModelId(selectedModel.value).toLowerCase()) ?? null
)

const filteredModels = computed(() => {
  const keyword = modelFilter.value.trim().toLowerCase()
  if (!keyword) return models.value
  return models.value.filter(
    (item) => item.id.toLowerCase().includes(keyword) || item.displayName.toLowerCase().includes(keyword)
  )
})

const allowedEfforts = computed(() => {
  if (matchedModel.value) return matchedModel.value.allowedEfforts
  return inferAllowedEfforts(selectedModel.value)
})

const filteredCandidates = computed(() => {
  const keyword = scopeFilter.value.trim()
  return keyword
    ? scopeCandidates.value.filter((candidate) => String(candidate.id).includes(keyword) || (candidate.name ?? '').includes(keyword))
    : scopeCandidates.value
})

watch(allowedEfforts, (allowed) => {
  if (allowed.length === 0) {
    selectedEffort.value = ''
    return
  }
  if (!allowed.includes(selectedEffort.value)) {
    selectedEffort.value = allowed.includes('medium') ? 'medium' : (allowed[0] ?? '')
  }
})

function normalizeModelId(value: string): string {
  const trimmed = value.trim()
  return trimmed.toLowerCase().startsWith('models/') ? trimmed.slice('models/'.length) : trimmed
}

function inferAllowedEfforts(modelId: string): string[] {
  const id = normalizeModelId(modelId).toLowerCase()
  if (!id) return ['default', 'minimal', 'low', 'medium', 'high', 'xhigh']
  if (id.startsWith('gemini-4-hydrogen') || id.startsWith('gemini-4-neon')) {
    return ['default', 'low', 'medium', 'high', 'xhigh']
  }
  if (id.startsWith('gemini-4-') || id.startsWith('gemini-4.')) return ['default', 'low', 'medium', 'high']
  if (id.startsWith('gemini-3-') || id.startsWith('gemini-3.')) return ['default', 'minimal', 'low', 'medium', 'high']
  if (id.startsWith('gemini-2.5-')) return ['default']
  if (id.startsWith('gemini-2.0-') || id.startsWith('gemini-1.5-') || id.startsWith('gemini-1.0-')) return []
  return ['default', 'minimal', 'low', 'medium', 'high', 'xhigh']
}

async function call<T>(path: string, init?: RequestInit): Promise<ApiResult<T>> {
  const result = await callApi<T>(props.context, path, init)
  if (result.error) error.value = result.error
  return result
}

async function loadUsage(days = usageDays.value) {
  usageDays.value = days
  usageLoading.value = true
  const result = await call<UsageSummary>(`/usage?days=${days}`)
  usageLoading.value = false
  if (result.ok) usage.value = result.body
}

function formatNumber(value: number): string {
  return value.toLocaleString('zh-CN')
}

function formatUsd(value: number): string {
  if (value === 0) return '$0.0000'
  if (value < 0.01) return `$${value.toFixed(5)}`
  return `$${value.toFixed(4)}`
}

function formatRate(rate: number, promptTokens: number): string {
  return promptTokens > 0 ? `${(rate * 100).toFixed(1)}%` : '—'
}

const usageRangeLabel = computed(() => USAGE_RANGES.find((item) => item.days === usageDays.value)?.label ?? `近 ${usageDays.value} 天`)

async function loadModelSettings() {
  const result = await call<ModelSettings>('/model')
  if (result.ok && result.body) {
    modelSettings.value = result.body
    selectedModel.value = result.body.model
    selectedEffort.value = result.body.effort
  }
}

async function fetchModels() {
  error.value = ''
  notice.value = ''
  modelsLoading.value = true
  const result = await call<ModelOption[]>('/models')
  modelsLoading.value = false
  if (result.ok) {
    models.value = result.body ?? []
    if (modelSettings.value && !modelSettings.value.hasApiKey) {
      modelSettings.value = { ...modelSettings.value, hasApiKey: true }
    }
    notice.value = `已拉取 ${models.value.length} 个可用模型`
  }
}

async function saveModel() {
  error.value = ''
  notice.value = ''
  const model = normalizeModelId(selectedModel.value)
  if (!model) {
    error.value = '请输入或选择一个模型名称'
    return
  }
  modelSaving.value = true
  const result = await call<ModelSettings>('/model', jsonBody('PUT', {
    model,
    effort: selectedEffort.value,
    thinking: matchedModel.value?.thinking ?? null
  }))
  modelSaving.value = false
  if (result.ok && result.body) {
    modelSettings.value = result.body
    selectedModel.value = result.body.model
    selectedEffort.value = result.body.effort
    notice.value = '模型与思考深度已保存，立即生效'
  }
}

function openSystemPromptModal() {
  error.value = ''
  notice.value = ''
  promptDraft.value = modelSettings.value?.systemPrompt ?? ''
  promptModalOpen.value = true
}

async function saveSystemPrompt() {
  error.value = ''
  notice.value = ''
  if (promptDraft.value.trim().length > SYSTEM_PROMPT_MAX_LENGTH) {
    error.value = `系统提示词不能超过 ${SYSTEM_PROMPT_MAX_LENGTH} 字`
    return
  }
  promptSaving.value = true
  const result = await call<ModelSettings>('/system-prompt', jsonBody('PUT', {
    systemPrompt: promptDraft.value
  }))
  promptSaving.value = false
  if (result.ok && result.body) {
    modelSettings.value = result.body
    promptModalOpen.value = false
    notice.value = result.body.systemPrompt ? '系统提示词已保存，立即生效' : '系统提示词已清空，立即生效'
  }
}

function describeSystemPrompt(): string {
  const prompt = modelSettings.value?.systemPrompt ?? ''
  if (!prompt) return '未设置'
  const firstLine = prompt.split('\n').find((line) => line.trim().length > 0)?.trim() ?? ''
  const preview = firstLine.length > 36 ? `${firstLine.slice(0, 36)}…` : firstLine
  return `已设置（${prompt.length} 字）· ${preview}`
}

async function refreshScopes() {
  const result = await call<Scopes>('/scope')
  if (result.ok) scopes.value = result.body
}

async function openScope(kind: ScopeKind) {
  error.value = ''
  notice.value = ''
  const current = scopes.value?.[kind]
  scopeEditing.value = kind
  scopeSelected.value = current ? [...current.ids] : []
  scopeCandidates.value = []
  scopeFilter.value = ''
  scopeLoading.value = true
  const result = await call<ScopeCandidate[]>(`/scope/${kind}/candidates`)
  scopeLoading.value = false
  if (result.ok) scopeCandidates.value = result.body ?? []
}

function toggleScopeSelected(id: number, checked: boolean) {
  scopeSelected.value = checked
    ? [...new Set([...scopeSelected.value, id])]
    : scopeSelected.value.filter((item) => item !== id)
}

async function saveScope() {
  if (!scopeEditing.value) return
  error.value = ''
  notice.value = ''
  scopeLoading.value = true
  const result = await call<void>(`/scope/${scopeEditing.value}`, jsonBody('PUT', { ids: scopeSelected.value }))
  scopeLoading.value = false
  if (!result.ok) return
  scopeEditing.value = null
  notice.value = '响应白名单已保存，立即生效'
  await refreshScopes()
}

function describeScope(kind: ScopeKind): string {
  const scope = scopes.value?.[kind]
  if (!scope) return '加载中'
  const noun = SCOPE_LABEL[kind].noun
  return scope.ids.length === 0 ? '空白名单（全部不响应）' : `白名单 · ${scope.ids.length} 个${noun}`
}

async function onSecretsUpdated(event: Event) {
  const detail = (event as CustomEvent<{ pluginId?: string }>).detail
  if (detail?.pluginId && detail.pluginId !== props.context.pluginId) return
  const hadKey = modelSettings.value?.hasApiKey ?? false
  await loadModelSettings()
  if (!hadKey && modelSettings.value?.hasApiKey && models.value.length === 0) {
    await fetchModels()
    notice.value = ''
  }
}

onMounted(async () => {
  window.addEventListener('shinano:plugin-secrets-updated', onSecretsUpdated)
  await Promise.all([loadUsage(), loadModelSettings(), refreshScopes()])
  if (modelSettings.value?.hasApiKey) {
    await fetchModels()
    notice.value = ''
  }
})

onBeforeUnmount(() => {
  window.removeEventListener('shinano:plugin-secrets-updated', onSecretsUpdated)
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <div v-if="error" role="alert" class="alert alert-error text-sm">{{ error }}</div>
    <div v-if="notice" role="status" class="alert alert-success text-sm">{{ notice }}</div>

    <section class="flex flex-col gap-3">
      <div class="flex flex-wrap items-center gap-2">
        <h3 class="font-semibold">用量统计</h3>
        <div class="join ml-auto" role="group" aria-label="统计范围">
          <button
            v-for="range in USAGE_RANGES"
            :key="range.days"
            class="btn join-item btn-xs"
            :class="{ 'btn-active': usageDays === range.days }"
            :disabled="usageLoading"
            @click="loadUsage(range.days)"
          >
            {{ range.label }}
          </button>
        </div>
        <button
          v-if="usage && (usage.byModel.length > 0 || usage.daily.length > 0)"
          class="btn btn-xs"
          @click="usageDetailModal = usage.byModel.length > 0 ? 'model' : 'daily'"
        >
          查看明细
        </button>
        <button class="btn btn-ghost btn-xs btn-square" aria-label="刷新用量统计" :disabled="usageLoading" @click="loadUsage()">
          <PhArrowsClockwise class="size-4" :class="{ 'animate-spin': usageLoading }" />
        </button>
      </div>

      <div v-if="usage" class="stats stats-vertical lg:stats-horizontal w-full border border-base-300 bg-base-100">
        <div class="stat">
          <div class="stat-title">估算成本（USD）</div>
          <div class="stat-value text-2xl tabular-nums">{{ formatUsd(usage.totals.costUsd) }}</div>
          <div class="stat-desc">按内置公开单价估算</div>
        </div>
        <div class="stat">
          <div class="stat-title">请求数</div>
          <div class="stat-value text-2xl tabular-nums">{{ formatNumber(usage.totals.requestCount) }}</div>
          <div class="stat-desc tabular-nums">成功 {{ formatNumber(usage.totals.successCount) }} · 失败 {{ formatNumber(usage.totals.failedCount) }}</div>
        </div>
        <div class="stat">
          <div class="stat-title">Token 消耗</div>
          <div class="stat-value text-2xl tabular-nums">{{ formatNumber(usage.totals.totalTokens) }}</div>
          <div class="stat-desc tabular-nums">
            输入 {{ formatNumber(usage.totals.promptTokens) }} · 输出 {{ formatNumber(usage.totals.candidatesTokens) }}<template v-if="usage.totals.thoughtsTokens > 0"> · 思考 {{ formatNumber(usage.totals.thoughtsTokens) }}</template>
          </div>
        </div>
        <div class="stat">
          <div class="stat-title">缓存命中率</div>
          <div class="stat-value text-2xl tabular-nums">{{ formatRate(usage.totals.cacheHitRate, usage.totals.promptTokens) }}</div>
          <div class="stat-desc tabular-nums">命中 {{ formatNumber(usage.totals.cachedTokens) }} / 输入 {{ formatNumber(usage.totals.promptTokens) }}</div>
        </div>
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <div class="flex flex-wrap items-center gap-2">
        <h3 class="font-semibold">模型与思考深度（Effort）</h3>
        <button class="btn btn-sm ml-auto" :disabled="modelsLoading" @click="fetchModels">
          <PhArrowsClockwise class="size-4" :class="{ 'animate-spin': modelsLoading }" />拉取可用模型
        </button>
      </div>
      <p v-if="modelSettings && !modelSettings.hasApiKey" class="text-sm text-warning">
        尚未配置 Gemini API Key，请先在上方「机密」卡片填写并保存后再拉取模型列表。
      </p>

      <div class="grid gap-4 md:grid-cols-2">
        <div class="flex flex-col gap-2">
          <label class="text-sm font-medium" for="gemini-model-input">模型名称（可选择或手动输入）</label>
          <input
            id="gemini-model-input"
            v-model="selectedModel"
            class="input input-sm w-full font-mono"
            placeholder="例如 gemini-4-hydrogen"
          />
          <template v-if="models.length > 0">
            <input
              v-model="modelFilter"
              class="input input-xs w-full"
              placeholder="筛选已拉取的模型"
              aria-label="筛选已拉取的模型"
            />
            <select
              class="select select-sm w-full font-mono"
              :value="matchedModel?.id ?? ''"
              aria-label="从 API 模型列表选择"
              @change="selectedModel = ($event.target as HTMLSelectElement).value || selectedModel"
            >
              <option value="">-- 从已拉取的 {{ filteredModels.length }} 个模型中选择 --</option>
              <option v-for="option in filteredModels" :key="option.id" :value="option.id">
                {{ option.id }}{{ option.displayName && option.displayName !== option.id ? `（${option.displayName}）` : '' }}
              </option>
            </select>
          </template>
          <p v-if="matchedModel?.description" class="text-xs opacity-70">{{ matchedModel.description }}</p>
        </div>

        <div class="flex flex-col gap-2">
          <label class="text-sm font-medium" for="gemini-effort-select">思考深度（Effort）</label>
          <select
            v-if="allowedEfforts.length > 0"
            id="gemini-effort-select"
            v-model="selectedEffort"
            class="select select-sm w-full"
          >
            <option v-for="effort in allowedEfforts" :key="effort" :value="effort">
              {{ EFFORT_LABELS[effort] ?? effort }}
            </option>
          </select>
          <p v-else class="rounded-box bg-base-200 px-3 py-2 text-sm opacity-70">
            当前模型不支持思考深度配置，请求时将不传递 thinkingConfig。
          </p>
          <p class="text-xs opacity-70">
            可选档位根据模型能力自动更新；切换模型时会自动校正不兼容的档位。
          </p>
        </div>
      </div>

      <div class="flex items-center gap-3">
        <button class="btn btn-primary btn-sm" :disabled="modelSaving" @click="saveModel">
          <span v-if="modelSaving" class="loading loading-spinner loading-xs"></span>保存模型设置
        </button>
        <span v-if="modelSettings" class="text-xs opacity-60 font-mono">
          当前生效：{{ modelSettings.model }}{{ modelSettings.effort ? ` · ${modelSettings.effort}` : '' }}
        </span>
      </div>

      <div class="flex flex-wrap items-center gap-3 rounded-box bg-base-200 px-4 py-3 text-sm">
        <span class="font-medium">系统提示词</span>
        <span class="min-w-0 flex-1 truncate opacity-70">{{ describeSystemPrompt() }}</span>
        <button class="btn btn-ghost btn-xs ml-auto" @click="openSystemPromptModal">
          <PhPencilSimple class="size-4" />编辑
        </button>
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <h3 class="font-semibold">响应范围（白名单）</h3>
      <p class="text-sm opacity-70">
        仅支持白名单模式：空名单默认不做任何响应；私聊仅限好友私聊（忽略临时会话），对 Bot Super User 同样生效。保存立即生效。
      </p>
      <div class="flex flex-col gap-2">
        <div
          v-for="kind in SCOPE_KINDS"
          :key="kind"
          class="flex flex-wrap items-center gap-3 rounded-box bg-base-200 px-4 py-3 text-sm"
        >
          <span class="font-medium">{{ SCOPE_LABEL[kind].title }}</span>
          <span class="opacity-70">{{ describeScope(kind) }}</span>
          <button class="btn btn-ghost btn-xs ml-auto" @click="openScope(kind)">
            <PhPencilSimple class="size-4" />修改
          </button>
        </div>
      </div>
    </section>

    <Teleport to="body">
      <div v-if="scopeEditing" class="modal modal-open" role="dialog" aria-labelledby="gemini-scope-title">
        <div class="modal-box flex max-h-[80vh] w-11/12 max-w-md flex-col gap-3">
          <header class="flex items-center">
            <h3 id="gemini-scope-title" class="text-lg font-semibold">{{ SCOPE_LABEL[scopeEditing].title }}白名单</h3>
            <button class="btn btn-ghost btn-sm btn-square ml-auto" aria-label="关闭" @click="scopeEditing = null">
              <PhX class="size-4" />
            </button>
          </header>
          <div class="flex items-center gap-2">
            <span class="badge badge-sm badge-primary">仅白名单</span>
            <button class="btn btn-ghost btn-sm ml-auto" :disabled="scopeSelected.length === 0" @click="scopeSelected = []">
              清空选择
            </button>
          </div>
          <p class="text-xs opacity-70">
            只响应勾选的{{ SCOPE_LABEL[scopeEditing].title }}，不勾选则全部不响应。
          </p>
          <input v-model="scopeFilter" class="input input-sm w-full" placeholder="按名称或号码筛选" aria-label="筛选" />
          <ul class="min-h-0 flex-1 overflow-y-auto">
            <li v-for="candidate in filteredCandidates" :key="candidate.id">
              <label class="flex cursor-pointer items-center gap-3 rounded-box px-2 py-1.5 hover:bg-base-200">
                <input
                  type="checkbox"
                  class="checkbox checkbox-sm"
                  :checked="scopeSelected.includes(candidate.id)"
                  @change="toggleScopeSelected(candidate.id, ($event.target as HTMLInputElement).checked)"
                />
                <span class="[overflow-wrap:anywhere]">
                  {{ candidate.name ?? (scopeEditing === 'group' ? '（Bot 已不在该群）' : '（已不是好友）') }}
                  <span class="ml-1 text-xs opacity-60 tabular-nums">{{ candidate.id }}</span>
                </span>
              </label>
            </li>
            <li v-if="scopeLoading" class="py-4 text-center">
              <span class="loading loading-spinner loading-sm"></span>
            </li>
            <li v-else-if="filteredCandidates.length === 0" class="py-4 text-center text-sm opacity-60">
              {{ scopeCandidates.length === 0 ? `没有${SCOPE_LABEL[scopeEditing].noun}（Bot 未连接）` : '没有匹配的项' }}
            </li>
          </ul>
          <footer class="flex items-center gap-2">
            <span class="text-sm opacity-70 tabular-nums">已选 {{ scopeSelected.length }} 个{{ SCOPE_LABEL[scopeEditing].noun }}</span>
            <button class="btn btn-ghost btn-sm ml-auto" @click="scopeEditing = null">取消</button>
            <button class="btn btn-primary btn-sm" :disabled="scopeLoading" @click="saveScope">保存</button>
          </footer>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div
        v-if="usage && usageDetailModal"
        class="modal modal-open"
        role="dialog"
        aria-labelledby="gemini-usage-modal-title"
        @click.self="usageDetailModal = null"
      >
        <div class="modal-box flex max-h-[80vh] w-11/12 max-w-4xl flex-col gap-3">
          <header class="flex flex-wrap items-center gap-2">
            <h3 id="gemini-usage-modal-title" class="text-lg font-semibold">
              {{ usageDetailModal === 'model' ? '按模型用量明细' : '按日用量明细' }}
            </h3>
            <span class="badge badge-sm badge-ghost">{{ usageRangeLabel }}</span>
            <div class="join ml-auto" role="group" aria-label="切换明细维度">
              <button
                class="btn join-item btn-xs"
                :class="{ 'btn-active': usageDetailModal === 'model' }"
                :disabled="usage.byModel.length === 0"
                @click="usageDetailModal = 'model'"
              >
                按模型（{{ usage.byModel.length }}）
              </button>
              <button
                class="btn join-item btn-xs"
                :class="{ 'btn-active': usageDetailModal === 'daily' }"
                :disabled="usage.daily.length === 0"
                @click="usageDetailModal = 'daily'"
              >
                按日（{{ usage.daily.length }}）
              </button>
            </div>
            <button class="btn btn-ghost btn-sm btn-square" aria-label="关闭" @click="usageDetailModal = null">
              <PhX class="size-4" />
            </button>
          </header>

          <div class="min-h-0 flex-1 overflow-auto">
            <table v-if="usageDetailModal === 'model'" class="table table-sm table-pin-rows">
              <thead>
                <tr>
                  <th>模型</th>
                  <th class="text-right">请求数</th>
                  <th class="text-right">输入（缓存）</th>
                  <th class="text-right">输出（思考）</th>
                  <th class="text-right">总计 Token</th>
                  <th class="text-right">缓存命中率</th>
                  <th class="text-right">成本 (USD)</th>
                </tr>
              </thead>
              <tbody class="tabular-nums">
                <tr v-for="row in usage.byModel" :key="row.model">
                  <td class="font-mono">{{ row.model }}</td>
                  <td class="text-right">{{ formatNumber(row.metrics.requestCount) }}</td>
                  <td class="text-right">{{ formatNumber(row.metrics.promptTokens) }}（{{ formatNumber(row.metrics.cachedTokens) }}）</td>
                  <td class="text-right">{{ formatNumber(row.metrics.candidatesTokens) }}（{{ formatNumber(row.metrics.thoughtsTokens) }}）</td>
                  <td class="text-right">{{ formatNumber(row.metrics.totalTokens) }}</td>
                  <td class="text-right">{{ formatRate(row.metrics.cacheHitRate, row.metrics.promptTokens) }}</td>
                  <td class="text-right font-mono">{{ formatUsd(row.metrics.costUsd) }}</td>
                </tr>
              </tbody>
            </table>

            <table v-else class="table table-sm table-pin-rows">
              <thead>
                <tr>
                  <th>日期</th>
                  <th class="text-right">请求数</th>
                  <th class="text-right">输入（缓存）</th>
                  <th class="text-right">输出（思考）</th>
                  <th class="text-right">总计 Token</th>
                  <th class="text-right">缓存命中率</th>
                  <th class="text-right">成本 (USD)</th>
                </tr>
              </thead>
              <tbody class="tabular-nums">
                <tr v-for="row in usage.daily" :key="row.date">
                  <td class="font-mono">{{ row.date }}</td>
                  <td class="text-right">{{ formatNumber(row.metrics.requestCount) }}</td>
                  <td class="text-right">{{ formatNumber(row.metrics.promptTokens) }}（{{ formatNumber(row.metrics.cachedTokens) }}）</td>
                  <td class="text-right">{{ formatNumber(row.metrics.candidatesTokens) }}（{{ formatNumber(row.metrics.thoughtsTokens) }}）</td>
                  <td class="text-right">{{ formatNumber(row.metrics.totalTokens) }}</td>
                  <td class="text-right">{{ formatRate(row.metrics.cacheHitRate, row.metrics.promptTokens) }}</td>
                  <td class="text-right font-mono">{{ formatUsd(row.metrics.costUsd) }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <footer class="flex items-center justify-end gap-2">
            <button class="btn btn-sm" @click="usageDetailModal = null">关闭</button>
          </footer>
        </div>
      </div>
    </Teleport>

    <Teleport to="body">
      <div
        v-if="promptModalOpen"
        class="modal modal-open"
        role="dialog"
        aria-labelledby="gemini-prompt-modal-title"
        @click.self="promptModalOpen = false"
      >
        <div class="modal-box flex max-h-[85vh] w-11/12 max-w-2xl flex-col gap-3">
          <header class="flex items-center gap-2">
            <h3 id="gemini-prompt-modal-title" class="text-lg font-semibold">编辑系统提示词</h3>
            <button class="btn btn-ghost btn-sm btn-square ml-auto" aria-label="关闭" @click="promptModalOpen = false">
              <PhX class="size-4" />
            </button>
          </header>
          <p class="text-xs opacity-70">每次对话请求都会附带此 System Instruction（支持多行文本）；留空保存则不发送。</p>
          <textarea
            v-model="promptDraft"
            class="textarea w-full min-h-64 flex-1 font-mono text-sm leading-6"
            rows="12"
            :maxlength="SYSTEM_PROMPT_MAX_LENGTH"
            placeholder="例如：你是一个严谨的技术助手，请始终使用简体中文回答……"
            aria-label="系统提示词"
          />
          <footer class="flex items-center gap-2">
            <span class="text-xs opacity-60 tabular-nums">{{ promptDraft.trim().length }} / {{ SYSTEM_PROMPT_MAX_LENGTH }} 字</span>
            <button class="btn btn-ghost btn-sm ml-auto" :disabled="promptSaving || promptDraft.length === 0" @click="promptDraft = ''">
              清空
            </button>
            <button class="btn btn-ghost btn-sm" :disabled="promptSaving" @click="promptModalOpen = false">取消</button>
            <button class="btn btn-primary btn-sm" :disabled="promptSaving" @click="saveSystemPrompt">
              <span v-if="promptSaving" class="loading loading-spinner loading-xs"></span>保存
            </button>
          </footer>
        </div>
      </div>
    </Teleport>
  </div>
</template>
