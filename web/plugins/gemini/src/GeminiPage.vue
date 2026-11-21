<script setup lang="ts">
import { PhArrowsClockwise, PhPencilSimple, PhX } from '@phosphor-icons/vue'
import { type ApiResult, callApi, jsonBody, type PluginPageContext } from '@shinano/plugin-sdk'
import { computed, onMounted, ref, watch } from 'vue'
import type { ModelOption, ModelSettings, ScopeCandidate, ScopeKind, Scopes } from './types'

const props = defineProps<{ context: PluginPageContext }>()

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

const error = ref('')
const notice = ref('')
const modelSettings = ref<ModelSettings | null>(null)
const selectedModel = ref('gemini-4-hydrogen')
const selectedEffort = ref('medium')
const modelFilter = ref('')
const models = ref<ModelOption[]>([])
const modelsLoading = ref(false)
const modelSaving = ref(false)

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

onMounted(async () => {
  await Promise.all([loadModelSettings(), refreshScopes()])
  if (modelSettings.value?.hasApiKey) {
    await fetchModels()
    notice.value = ''
  }
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <div v-if="error" role="alert" class="alert alert-error text-sm">{{ error }}</div>
    <div v-if="notice" role="status" class="alert alert-success text-sm">{{ notice }}</div>

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
  </div>
</template>
