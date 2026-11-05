<script setup lang="ts">
import { PhX } from '@phosphor-icons/vue'
import type { PluginPageContext } from '@shinano/plugin-sdk'
import { computed, onMounted, reactive, ref } from 'vue'
import { formatSize } from './format'
import type { ManualInfo, Review, ReviewImage } from './types'

const props = defineProps<{ context: PluginPageContext; apiBase: string }>()
const emit = defineEmits<{ close: []; changed: [] }>()

type Action = 'replace' | 'accept' | 'reject'

const ORIGIN_LABEL: Record<Review['origin'], string> = {
  Upload: '面板上传',
  Zip: 'zip 上传',
  Pixiv: 'AD 导入',
  Tool: '工具导入'
}

const ids = ref<number[]>([])
const skipped = ref<number[]>([])
const review = ref<Review | null>(null)
const loading = ref(true)
const busy = ref(false)
const error = ref('')
const rejecting = ref(false)
const form = reactive({ title: '', author: '', authorId: '', pid: '', page: '', tags: '', nsfw: false })

const position = computed(() => (review.value ? ids.value.indexOf(review.value.imageId) + 1 : 0))
const sameSha = computed(() => review.value?.candidate?.sha256 === review.value?.current.sha256)
const source = computed(() => {
  const value = review.value
  if (!value) return ''
  if (value.method === 'Pixiv') return '按 pid 查询 Pixiv'
  const similarity = value.similarity === null ? '' : `，相似度 ${value.similarity.toFixed(2)}%`
  return `SauceNAO 以图搜图${similarity}`
})

async function call<T>(path: string, init?: RequestInit): Promise<{ ok: boolean; body: T | null }> {
  error.value = ''
  const response = await props.context.api(path, init)
  if (response.ok) return { ok: true, body: response.status === 204 ? null : ((await response.json()) as T) }
  const body = (await response.json().catch(() => ({}))) as { error?: string }
  error.value = body.error ?? `请求失败（${response.status}）`
  return { ok: false, body: null }
}

async function loadQueue() {
  loading.value = true
  ids.value = (await call<number[]>('/reviews')).body ?? []
  await showNext()
  loading.value = false
}

// 显示第一张没有跳过的图片；它已被别处处理（404）时从队列移除再找下一张。
async function showNext() {
  rejecting.value = false
  resetForm()
  for (;;) {
    const next = ids.value.find((id) => !skipped.value.includes(id))
    if (next === undefined) {
      review.value = null
      return
    }

    const result = await call<Review>(`/reviews/${next}`)
    if (result.body) {
      review.value = result.body
      return
    }

    ids.value = ids.value.filter((id) => id !== next)
    error.value = ''
  }
}

async function act(action: Action) {
  const current = review.value
  if (!current || busy.value) return
  busy.value = true
  const init: RequestInit = action === 'reject'
    ? { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(manualInfo()) }
    : { method: 'POST' }
  const result = await call(`/reviews/${current.imageId}/${action}`, init)
  if (result.ok) {
    ids.value = ids.value.filter((id) => id !== current.imageId)
    emit('changed')
    await showNext()
  }

  busy.value = false
}

function skip() {
  if (!review.value || busy.value) return
  skipped.value = [...skipped.value, review.value.imageId]
  error.value = ''
  void showNext()
}

function resetForm() {
  Object.assign(form, { title: '', author: '', authorId: '', pid: '', page: '', tags: '', nsfw: false })
}

function manualInfo(): ManualInfo {
  return {
    title: form.title.trim() || null,
    author: form.author.trim() || null,
    authorId: integer(form.authorId),
    pid: integer(form.pid),
    page: integer(form.page),
    tags: form.tags.split(/[\s,，、]+/).map((tag) => tag.trim()).filter((tag) => tag.length > 0),
    nsfw: form.nsfw
  }
}

function integer(text: string): number | null {
  return /^\d+$/.test(text.trim()) ? Number(text.trim()) : null
}

function describe(image: ReviewImage): string {
  const format = image.mediaType.replace('image/', '').toUpperCase()
  const size = image.width !== null && image.height !== null ? ` · ${image.width}×${image.height}` : ''
  return `${format}${size} · ${formatSize(image.size)}`
}

function work(value: Review): string {
  if (value.workSite === null || value.workId === null) return '没有可下载原图的来源'
  return value.workPage === null ? `${value.workSite} ${value.workId}` : `${value.workSite} ${value.workId} 第 ${value.workPage} 页`
}

function time(text: string): string {
  return new Date(text).toLocaleString('zh-CN', { hour12: false })
}

onMounted(() => void loadQueue())
</script>

<template>
  <!-- 挂到 body 下：留在插件页面内时处于内容区的层叠上下文，遮罩盖不住侧边栏与日志面板。 -->
  <Teleport to="body">
    <div class="modal modal-open" role="dialog" aria-labelledby="hso-review-title">
      <div class="modal-box flex w-11/12 max-w-5xl flex-col gap-4">
        <header class="flex items-center gap-3">
          <h3 id="hso-review-title" class="text-lg font-semibold">作品信息审核</h3>
          <span v-if="review" class="text-sm opacity-70 tabular-nums">第 {{ position }} 张，队列中共 {{ ids.length }} 张</span>
          <button class="btn btn-ghost btn-sm btn-square ml-auto" aria-label="关闭" @click="emit('close')"><PhX class="size-4" /></button>
        </header>

        <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>

        <p v-if="loading" class="flex items-center gap-2 py-10 justify-center opacity-70"><span class="loading loading-spinner loading-sm"></span>读取队列</p>
        <p v-else-if="!review" class="py-10 text-center opacity-70">
          {{ ids.length === 0 ? '审核队列已处理完' : `剩下的 ${ids.length} 张都已跳过，关闭后重新打开可再次处理` }}
        </p>

        <template v-else>
          <div class="grid grid-cols-2 gap-4">
            <figure class="flex flex-col gap-2">
              <figcaption class="flex items-baseline gap-2">
                <span class="font-semibold">图库中的图片</span>
                <span class="text-xs opacity-60 tabular-nums">#{{ review.imageId }}</span>
              </figcaption>
              <a :href="`${apiBase}/images/${review.imageId}/original`" target="_blank" rel="noopener" class="block h-80 overflow-hidden rounded-box bg-base-200" aria-label="查看图库中的原图">
                <img :src="`${apiBase}/images/${review.imageId}/thumbnail`" class="size-full object-contain" alt="图库中的图片" />
              </a>
              <dl class="grid grid-cols-[4.5rem_1fr] items-baseline gap-x-3 gap-y-1 text-sm">
                <dt class="opacity-60">格式</dt><dd class="tabular-nums">{{ describe(review.current) }}</dd>
                <dt class="opacity-60">SHA256</dt><dd class="font-mono text-xs break-all">{{ review.current.sha256 }}</dd>
                <dt class="opacity-60">文件名</dt><dd class="[overflow-wrap:anywhere]">{{ review.fileName ?? '（无）' }}</dd>
                <dt class="opacity-60">导入</dt><dd>{{ ORIGIN_LABEL[review.origin] }} · {{ time(review.importedAt) }}</dd>
              </dl>
            </figure>

            <figure class="flex flex-col gap-2">
              <figcaption class="flex items-baseline gap-2">
                <span class="font-semibold">查询获取的原图</span>
                <span v-if="review.candidate" class="badge badge-sm" :class="sameSha ? 'badge-success' : 'badge-warning'">{{ sameSha ? 'SHA256 一致' : 'SHA256 不一致' }}</span>
              </figcaption>
              <a v-if="review.candidate" :href="`${apiBase}/reviews/${review.imageId}/candidate`" target="_blank" rel="noopener" class="block h-80 overflow-hidden rounded-box bg-base-200" aria-label="查看查询获取的原图">
                <img :src="`${apiBase}/reviews/${review.imageId}/candidate/thumbnail`" class="size-full object-contain" alt="查询获取的原图" />
              </a>
              <div v-else class="flex h-80 flex-col items-center justify-center gap-1 rounded-box bg-base-200 text-sm">
                <span class="font-semibold text-error-strong">原图获取失败</span>
                <span class="opacity-70">{{ review.fetchError ?? '没有可对比的原图' }}</span>
              </div>
              <dl class="grid grid-cols-[4.5rem_1fr] items-baseline gap-x-3 gap-y-1 text-sm">
                <template v-if="review.candidate">
                  <dt class="opacity-60">格式</dt><dd class="tabular-nums">{{ describe(review.candidate) }}</dd>
                  <dt class="opacity-60">SHA256</dt><dd class="font-mono text-xs break-all" :class="sameSha ? '' : 'text-warning-strong'">{{ review.candidate.sha256 }}</dd>
                </template>
                <dt class="opacity-60">来源</dt><dd>{{ work(review) }}</dd>
                <dt class="opacity-60">查询于</dt><dd>{{ time(review.createdAt) }}</dd>
              </dl>
            </figure>
          </div>

          <section class="tile flex flex-col gap-2 p-3">
            <div class="flex items-baseline gap-2">
              <h4 class="font-semibold">查询到的作品信息</h4>
              <span class="text-xs opacity-60">{{ source }}</span>
              <span class="badge badge-sm ml-auto" :class="review.nsfw ? 'badge-error' : ''">{{ review.nsfw ? 'NSFW' : '非 NSFW' }}</span>
            </div>
            <dl class="grid grid-cols-[4.5rem_1fr] items-baseline gap-x-3 gap-y-1 text-sm">
              <dt class="opacity-60">来源</dt><dd>{{ review.sourceName ?? '—' }}</dd>
              <dt class="opacity-60">标题</dt><dd class="[overflow-wrap:anywhere]">{{ review.title ?? '—' }}</dd>
              <dt class="opacity-60">作者</dt><dd class="[overflow-wrap:anywhere]">{{ review.author ?? '—' }}</dd>
              <dt class="opacity-60">UID</dt><dd class="tabular-nums">{{ review.authorId ?? '—' }}</dd>
              <dt class="opacity-60">PID</dt><dd class="tabular-nums">{{ review.pid === null ? '—' : review.page === null ? review.pid : `${review.pid}（第 ${review.page} 页）` }}</dd>
              <dt class="opacity-60">TAG</dt>
              <dd class="flex flex-wrap gap-1">
                <span v-for="tag in review.tags" :key="tag" class="rounded-field border border-base-content/30 px-2 text-xs leading-5">{{ tag }}</span>
                <span v-if="review.tags.length === 0">—</span>
              </dd>
            </dl>
          </section>

          <section v-if="rejecting" class="tile flex flex-col gap-3 p-3">
            <div class="flex items-baseline gap-2">
              <h4 class="font-semibold">否决并手动补全</h4>
              <span class="text-xs opacity-60">查询到的信息将被丢弃；全部留空则记为查无信息。之后不再自动查询。</span>
            </div>
            <div class="grid grid-cols-2 gap-3">
              <label class="flex flex-col gap-1 text-sm">标题<input v-model="form.title" class="input input-sm w-full" placeholder="留空" /></label>
              <label class="flex flex-col gap-1 text-sm">作者<input v-model="form.author" class="input input-sm w-full" placeholder="留空" /></label>
              <label class="flex flex-col gap-1 text-sm">UID<input v-model="form.authorId" class="input input-sm w-full" inputmode="numeric" placeholder="Pixiv 作者 ID" /></label>
              <div class="grid grid-cols-[1fr_6rem] gap-2">
                <label class="flex flex-col gap-1 text-sm">PID<input v-model="form.pid" class="input input-sm w-full" inputmode="numeric" placeholder="Pixiv 作品号" /></label>
                <label class="flex flex-col gap-1 text-sm">页号<input v-model="form.page" class="input input-sm w-full" inputmode="numeric" placeholder="0" /></label>
              </div>
              <label class="col-span-2 flex flex-col gap-1 text-sm">TAG<input v-model="form.tags" class="input input-sm w-full" placeholder="以空格或逗号分隔" /></label>
              <label class="flex items-center gap-2 text-sm"><input v-model="form.nsfw" type="checkbox" class="toggle toggle-sm" /> NSFW</label>
            </div>
            <div class="flex justify-end gap-2">
              <button class="btn btn-neutral btn-sm" :disabled="busy" @click="rejecting = false">取消</button>
              <button class="btn btn-error btn-sm" :disabled="busy" @click="act('reject')">否决并保存</button>
            </div>
          </section>

          <footer class="flex flex-wrap items-center gap-2">
            <button class="btn btn-primary" :disabled="busy || !review.candidate" title="用查询获取的原图替换图库图片，并确认作品信息" @click="act('replace')">
              <span v-if="busy" class="loading loading-spinner loading-xs"></span>覆盖
            </button>
            <button class="btn btn-secondary" :disabled="busy" title="保留图库原有图片，确认作品信息" @click="act('accept')">只确认信息</button>
            <button class="btn btn-error" :disabled="busy" title="丢弃查询到的信息，可手动补全" @click="rejecting = true">否决</button>
            <button class="btn btn-neutral ml-auto" :disabled="busy" title="留在队列中，稍后处理" @click="skip">跳过</button>
          </footer>
        </template>
      </div>
    </div>
  </Teleport>
</template>
