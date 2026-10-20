<script setup lang="ts">
import { PhImage, PhTrash } from '@phosphor-icons/vue'
import { computed, ref } from 'vue'
import { api, describeError, request } from '../api'
import { appearance, backgroundUrl, refreshAppearance } from '../appearance'

const error = ref('')
const busy = ref(false)
const hasBackground = computed(() => backgroundUrl.value !== null)

// 上传或删除背景图期间锁定表单，完成后重新加载背景图。
async function changeBackground(action: () => Promise<void>): Promise<void> {
  busy.value = true
  error.value = ''
  try {
    await action()
    await refreshAppearance(true)
  } catch (e) {
    error.value = describeError(e)
  } finally {
    busy.value = false
  }
}

// 不透明度与模糊在本地已生效，保存时不锁定表单、不重新加载背景图；失败时恢复为服务端的值。
async function save(): Promise<void> {
  error.value = ''
  const { panelOpacity, cardOpacity, veilOpacity, blur } = appearance.value
  try {
    await api.put('/api/appearance', { panelOpacity, cardOpacity, veilOpacity, blur })
  } catch (e) {
    error.value = describeError(e)
    await refreshAppearance().catch(() => undefined)
  }
}

function upload(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file) return Promise.resolve()
  const body = new FormData()
  body.append('file', file)
  return changeBackground(async () => {
    await request('/api/appearance/background', { method: 'POST', body })
    input.value = ''
  })
}

function removeBackground(): Promise<void> {
  return changeBackground(() => api.delete('/api/appearance/background'))
}
</script>

<template>
  <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>

  <section class="surface card">
    <div class="card-body gap-4">
      <h2 class="card-title text-lg">背景图</h2>
      <p class="text-sm opacity-70">不设置时页面与面板为纯色。支持 PNG、JPEG、GIF、WebP，不超过 10 MiB；图片等比铺满页面，保存在部署数据目录。</p>
      <div class="flex flex-wrap items-start gap-4">
        <div class="tile grid aspect-video w-72 max-w-full place-items-center overflow-hidden">
          <img v-if="backgroundUrl" :src="backgroundUrl" alt="当前背景图" class="size-full object-cover" />
          <span v-else class="flex flex-col items-center gap-1 text-sm opacity-60"><PhImage class="size-8" aria-hidden="true" />未设置背景图</span>
        </div>
        <div class="flex flex-col gap-2">
          <label class="fieldset-legend" for="background-file">{{ hasBackground ? '更换背景图' : '上传背景图' }}</label>
          <input id="background-file" type="file" accept="image/png,image/jpeg,image/gif,image/webp" class="file-input" :disabled="busy" @change="upload" />
          <button v-if="hasBackground" class="btn btn-ghost w-fit border-[var(--line)]" :disabled="busy" @click="removeBackground">
            <PhTrash class="size-4" aria-hidden="true" />删除背景图
          </button>
        </div>
      </div>
    </div>
  </section>

  <section class="surface card">
    <div class="card-body gap-4">
      <h2 class="card-title text-lg">透明度与模糊</h2>
      <p class="text-sm opacity-70">只在设置背景图后生效。背景图本身始终不透明；模糊会让面板后面的背景看不清细节，但文字更易读。</p>
      <fieldset class="fieldset max-w-xl" :disabled="!hasBackground || busy">
        <label class="label cursor-pointer justify-start gap-3" for="appearance-blur">
          <input id="appearance-blur" v-model="appearance.blur" type="checkbox" class="toggle toggle-primary" @change="save" />
          磨砂模糊
        </label>
        <label class="fieldset-legend" for="panel-opacity">侧边面板不透明度：{{ appearance.panelOpacity }}%</label>
        <input id="panel-opacity" v-model.number="appearance.panelOpacity" type="range" min="0" max="100" class="range range-primary range-sm" @change="save" />
        <p class="label whitespace-normal">侧边栏与右侧日志面板的底色。</p>
        <label class="fieldset-legend" for="card-opacity">内容区卡片不透明度：{{ appearance.cardOpacity }}%</label>
        <input id="card-opacity" v-model.number="appearance.cardOpacity" type="range" min="0" max="100" class="range range-primary range-sm" @change="save" />
        <p class="label whitespace-normal">页面中间的卡片，以及固定的筛选栏与底部操作栏的底色。</p>
        <label class="fieldset-legend" for="veil-opacity">内容区遮罩不透明度：{{ appearance.veilOpacity }}%</label>
        <input id="veil-opacity" v-model.number="appearance.veilOpacity" type="range" min="0" max="100" class="range range-primary range-sm" @change="save" />
        <p class="label whitespace-normal">页面中间内容区的底色，用来压暗或提亮卡片之间露出的背景。</p>
      </fieldset>
    </div>
  </section>
</template>
