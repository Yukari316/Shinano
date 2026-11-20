<script setup lang="ts">
import { PhArrowsClockwise, PhCaretLeft, PhCaretRight, PhListChecks, PhProhibit, PhTrash, PhUploadSimple } from '@phosphor-icons/vue'
import { callApi, jsonBody, pluginApiBase, type PluginPageContext } from '@shinano/plugin-sdk'
import { computed, onMounted, ref, watch } from 'vue'
import { formatSize } from './format'
import ReviewDialog from './ReviewDialog.vue'
import type { GalleryImage, Group, ImagePage, ImportOutcome, ImportResult, InfoState } from './types'

const props = defineProps<{ context: PluginPageContext }>()

const PAGE_SIZE = 20
const OUTCOME_LABEL: Record<ImportOutcome, string> = {
  Imported: '已导入',
  Duplicate: '已存在，跳过',
  Unsupported: '不是 JPEG、PNG、GIF 或 WebP',
  Failed: '写入失败'
}
const ORIGIN_LABEL: Record<GalleryImage['origin'], string> = {
  Upload: '上传',
  Zip: 'zip',
  Pixiv: 'AD',
  Tool: '工具'
}
// 没有标题、作者与 pid 时在图库网格中代替显示的补全状态。
const STATE_LABEL: Record<InfoState, string> = {
  Pending: '作品信息待补全',
  Review: '作品信息待审核',
  Found: '作品信息已确认',
  NotFound: '没有作品信息'
}

const error = ref('')
const uploading = ref(false)
const results = ref<ImportResult[]>([])
const groups = ref<Group[]>([])
const images = ref<GalleryImage[]>([])
const total = ref(0)
const page = ref(1)
const nsfw = ref<'' | 'true' | 'false'>('')
const hasPid = ref<'' | 'true' | 'false'>('')
const blocked = ref<'' | 'true' | 'false'>('')
const keyword = ref('')
const reviewCount = ref(0)
const reviewing = ref(false)

const pages = computed(() => Math.max(1, Math.ceil(total.value / PAGE_SIZE)))
const imported = computed(() => results.value.filter((result) => result.outcome === 'Imported').length)
const apiBase = computed(() => pluginApiBase(props.context.pluginId))

async function call<T>(path: string, init?: RequestInit): Promise<T | null> {
  const result = await callApi<T>(props.context, path, init)
  error.value = result.error ?? ''
  return result.ok ? result.body : null
}

async function upload(event: Event) {
  const input = event.target as HTMLInputElement
  if (!input.files || input.files.length === 0) return
  const form = new FormData()
  for (const file of input.files) form.append('files', file, file.name)
  uploading.value = true
  results.value = (await call<ImportResult[]>('/images', { method: 'POST', body: form })) ?? []
  uploading.value = false
  input.value = ''
  page.value = 1
  await loadImages()
}

async function loadGroups() {
  groups.value = (await call<Group[]>('/groups')) ?? []
}

async function saveGroup(group: Group) {
  await call(`/groups/${group.groupId}`, jsonBody('PUT', { blocked: group.blocked, allowNsfw: group.allowNsfw }))
  if (error.value) await loadGroups()
}

// 只采用最近一次请求的结果：快速切换筛选或翻页时，较慢的旧响应不覆盖新结果。
let imagesRequest = 0

async function loadImages() {
  const query = new URLSearchParams({ page: String(page.value) })
  if (nsfw.value) query.set('nsfw', nsfw.value)
  if (hasPid.value) query.set('hasPid', hasPid.value)
  if (blocked.value) query.set('blocked', blocked.value)
  if (keyword.value.trim()) query.set('keyword', keyword.value.trim())
  const request = ++imagesRequest
  const result = await call<ImagePage>(`/images?${query}`)
  if (request !== imagesRequest) return
  images.value = result?.items ?? []
  total.value = result?.total ?? 0
}

async function loadReviewCount() {
  reviewCount.value = ((await call<number[]>('/reviews')) ?? []).length
}

function closeReview() {
  reviewing.value = false
  void Promise.all([loadReviewCount(), loadImages()])
}

async function setNsfw(image: GalleryImage, value: boolean) {
  await call(`/images/${image.id}/nsfw`, jsonBody('PUT', { nsfw: value }))
  await loadImages()
}

async function clearBlocked(image: GalleryImage) {
  await call(`/images/${image.id}/blocked`, { method: 'DELETE' })
  await loadImages()
}

async function remove(image: GalleryImage) {
  if (!window.confirm(`删除图片 ${describe(image)}？对象会从 RustFS 删除，无法恢复。`)) return
  await call(`/images/${image.id}`, { method: 'DELETE' })
  if (images.value.length === 1 && page.value > 1) page.value -= 1
  await loadImages()
}

function describe(image: GalleryImage): string {
  if (image.pid !== null) return image.page !== null ? `${image.pid} - p${image.page}` : String(image.pid)
  return image.fileName ?? `#${image.id}`
}

let keywordTimer: number | undefined
watch([nsfw, hasPid, blocked], () => {
  page.value = 1
  void loadImages()
})
watch(keyword, () => {
  window.clearTimeout(keywordTimer)
  keywordTimer = window.setTimeout(() => {
    page.value = 1
    void loadImages()
  }, 300)
})
watch(page, () => void loadImages())

onMounted(async () => {
  await Promise.all([loadGroups(), loadImages(), loadReviewCount()])
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>

    <div class="flex items-center gap-3">
      <p class="text-sm opacity-70">查到的来源原图与图库图片不一致或下载失败时，作品信息进入审核队列；审核前抽到这张图只发图片，不发信息卡。</p>
      <button class="btn btn-primary btn-sm ml-auto shrink-0" @click="reviewing = true"><PhListChecks class="size-4" />审核队列（{{ reviewCount }}）</button>
    </div>
    <ReviewDialog v-if="reviewing" :context="context" @changed="reviewCount = Math.max(0, reviewCount - 1)" @close="closeReview" />

    <section class="flex flex-col gap-3">
      <h3 class="font-semibold">上传</h3>
      <p class="text-sm opacity-70">可一次选择多张图片或 zip；按文件名 <code>&lt;pid&gt;_p&lt;页号&gt;</code> 识别 Pixiv 作品，作品信息在图片被抽中后或由每日任务在后台补全。相同内容的图片只保存一份。</p>
      <label class="btn btn-primary btn-sm w-fit" :class="{ 'btn-disabled': uploading }">
        <span v-if="uploading" class="loading loading-spinner loading-xs"></span>
        <PhUploadSimple v-else class="size-4" />{{ uploading ? '上传中' : '选择文件' }}
        <input type="file" class="hidden" multiple accept="image/jpeg,image/png,image/gif,image/webp,.zip" :disabled="uploading" @change="upload" />
      </label>
      <div v-if="results.length > 0" class="tile flex flex-col gap-1 p-3 text-sm">
        <p>共 {{ results.length }} 个文件，导入 {{ imported }} 张。</p>
        <ul class="max-h-48 overflow-y-auto">
          <li v-for="(result, index) in results.filter((item) => item.outcome !== 'Imported')" :key="index" class="flex gap-2">
            <span class="[overflow-wrap:anywhere]">{{ result.fileName ?? '（无法读取的 zip）' }}</span>
            <span :class="result.outcome === 'Duplicate' ? 'opacity-60' : 'text-error-strong'">{{ OUTCOME_LABEL[result.outcome] }}</span>
          </li>
        </ul>
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <div class="flex items-center gap-2">
        <h3 class="font-semibold">群设置</h3>
        <button class="btn btn-ghost btn-xs" aria-label="刷新群列表" @click="loadGroups"><PhArrowsClockwise class="size-4" />刷新</button>
      </div>
      <p class="text-sm opacity-70">屏蔽的群中全部 Hso 指令不响应；不允许 NSFW 的群只抽取非 NSFW 图片。新群默认不屏蔽、不允许 NSFW。</p>
      <div class="max-h-72 overflow-y-auto">
        <table class="table table-sm">
          <thead>
            <tr><th>群</th><th class="w-24">屏蔽</th><th class="w-28">允许 NSFW</th></tr>
          </thead>
          <tbody>
            <tr v-for="group in groups" :key="group.groupId">
              <td>{{ group.name ?? '（Bot 已不在该群）' }}<span class="ml-1 text-xs opacity-60 tabular-nums">{{ group.groupId }}</span></td>
              <td><input v-model="group.blocked" type="checkbox" class="toggle toggle-sm" :aria-label="`屏蔽 ${group.groupId}`" @change="saveGroup(group)" /></td>
              <td><input v-model="group.allowNsfw" type="checkbox" class="toggle toggle-sm" :aria-label="`允许 NSFW ${group.groupId}`" @change="saveGroup(group)" /></td>
            </tr>
            <tr v-if="groups.length === 0"><td colspan="3" class="py-4 text-center opacity-60">没有群（Bot 未连接时只显示已保存设置的群）</td></tr>
          </tbody>
        </table>
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <h3 class="font-semibold">筛选</h3>
      <div class="flex flex-wrap items-end gap-3">
        <select v-model="nsfw" class="select select-sm w-32" aria-label="按 NSFW 筛选">
          <option value="">全部</option>
          <option value="true">NSFW</option>
          <option value="false">非 NSFW</option>
        </select>
        <select v-model="hasPid" class="select select-sm w-32" aria-label="按 pid 筛选">
          <option value="">有无 pid</option>
          <option value="true">有 pid</option>
          <option value="false">没有 pid</option>
        </select>
        <select v-model="blocked" class="select select-sm w-32" aria-label="按拦截状态筛选">
          <option value="">拦截状态</option>
          <option value="true">被拦截过</option>
          <option value="false">未被拦截</option>
        </select>
        <input v-model="keyword" class="input input-sm w-64" placeholder="pid、标题、作者或文件名" aria-label="关键字" />
      </div>
    </section>

    <section class="flex flex-col gap-3">
      <div class="flex items-center gap-2">
        <h3 class="font-semibold">图库</h3>
        <span class="text-sm opacity-70 tabular-nums">共 {{ total }} 张</span>
      </div>
      <ul class="grid grid-cols-[repeat(auto-fill,minmax(12rem,1fr))] gap-3">
        <li v-for="image in images" :key="image.id" class="tile flex flex-col gap-2 p-2">
          <a :href="`${apiBase}/images/${image.id}/original`" target="_blank" rel="noopener" class="block aspect-square overflow-hidden bg-base-200" :aria-label="`查看原图 ${describe(image)}`">
            <img :src="`${apiBase}/images/${image.id}/thumbnail`" loading="lazy" class="size-full object-contain" :alt="describe(image)" />
          </a>
          <div class="flex flex-col gap-0.5 text-sm">
            <span class="font-semibold [overflow-wrap:anywhere]">{{ image.title ?? describe(image) }}</span>
            <span class="text-xs opacity-70 [overflow-wrap:anywhere]">
              {{ [image.author, image.pid !== null ? describe(image) : image.sourceName].filter(Boolean).join(' · ') || STATE_LABEL[image.infoState] }}
            </span>
            <span v-if="image.infoState === 'Review'" class="text-xs text-warning-strong">待审核</span>
            <span class="text-xs opacity-60 tabular-nums">{{ ORIGIN_LABEL[image.origin] }} · {{ formatSize(image.size) }}</span>
          </div>
          <div class="flex flex-wrap items-center gap-2">
            <label class="flex items-center gap-1 text-xs">
              <input type="checkbox" class="toggle toggle-xs" :checked="image.nsfw" :aria-label="`NSFW ${describe(image)}`" @change="setNsfw(image, ($event.target as HTMLInputElement).checked)" />
              NSFW<span v-if="image.nsfwManual" class="opacity-60">（手动）</span>
            </label>
            <button v-if="image.blocked" class="btn btn-ghost btn-xs text-warning-strong" :aria-label="`清除拦截标记 ${describe(image)}`" title="被拦截过，发送前先改像素；点击清除标记" @click="clearBlocked(image)"><PhProhibit class="size-4" />被拦截</button>
            <button class="btn btn-ghost btn-xs ml-auto text-error-strong" :aria-label="`删除 ${describe(image)}`" @click="remove(image)"><PhTrash class="size-4" /></button>
          </div>
        </li>
      </ul>
      <p v-if="images.length === 0" class="py-6 text-center text-sm opacity-60">没有图片</p>
      <div v-if="pages > 1" class="join self-center">
        <button class="join-item btn btn-sm" :disabled="page <= 1" aria-label="上一页" @click="page -= 1"><PhCaretLeft class="size-4" /></button>
        <span class="join-item btn btn-sm btn-disabled tabular-nums">{{ page }} / {{ pages }}</span>
        <button class="join-item btn btn-sm" :disabled="page >= pages" aria-label="下一页" @click="page += 1"><PhCaretRight class="size-4" /></button>
      </div>
    </section>
  </div>
</template>
