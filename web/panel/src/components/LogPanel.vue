<script setup lang="ts">
import { PhCaretDoubleLeft, PhCaretDoubleRight, PhPause, PhPlay } from '@phosphor-icons/vue'
import { onBeforeUnmount, ref, watch } from 'vue'
import { logsPaused, subscribeLogs, togglePause, warningCount } from '../logStream'
import LogList from './LogList.vue'

const WIDTH_KEY = 'shinano-log-width'
const COLLAPSED_KEY = 'shinano-log-collapsed'
const MIN_WIDTH = 280
const MAX_WIDTH = 720
const DEFAULT_WIDTH = 380
const KEY_STEP = 24

// 宽度与开合只是本浏览器的偏好，读写失败时使用默认值。
function load(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function save(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    // 浏览器禁止存储时只在本次会话生效。
  }
}

const width = ref(Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, Number(load(WIDTH_KEY)) || DEFAULT_WIDTH)))
const collapsed = ref(load(COLLAPSED_KEY) === '1')
watch(width, (value) => save(WIDTH_KEY, String(value)))
watch(collapsed, (value) => save(COLLAPSED_KEY, value ? '1' : '0'))

const release = subscribeLogs()
onBeforeUnmount(release)

let dragStartX = 0
let dragStartWidth = 0

function startDrag(event: PointerEvent): void {
  dragStartX = event.clientX
  dragStartWidth = width.value
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}

function drag(event: PointerEvent): void {
  if (!(event.currentTarget as HTMLElement).hasPointerCapture(event.pointerId)) return
  // 面板停靠在右侧，向左拖动变宽。
  width.value = Math.min(MAX_WIDTH, Math.max(MIN_WIDTH, dragStartWidth + dragStartX - event.clientX))
}

function resizeByKey(event: KeyboardEvent): void {
  if (event.key === 'ArrowLeft') width.value = Math.min(MAX_WIDTH, width.value + KEY_STEP)
  else if (event.key === 'ArrowRight') width.value = Math.max(MIN_WIDTH, width.value - KEY_STEP)
  else return
  event.preventDefault()
}
</script>

<template>
  <button
    v-if="collapsed"
    class="surface btn h-auto w-11 shrink-0 flex-col gap-2 self-stretch rounded-box px-0 py-4"
    title="展开实时日志"
    @click="collapsed = false"
  >
    <PhCaretDoubleLeft class="size-4" aria-hidden="true" />
    <span class="[writing-mode:vertical-rl]">实时日志</span>
    <span v-if="warningCount > 0" class="badge badge-warning badge-sm">{{ warningCount }}</span>
  </button>
  <aside
    v-else
    class="surface relative flex shrink-0 flex-col rounded-box"
    :style="{ width: `${width}px` }"
    aria-label="实时日志"
  >
    <div
      class="absolute -left-2 top-1/2 h-16 w-2 -translate-y-1/2 cursor-ew-resize rounded-full bg-base-content/25 hover:bg-base-content/45 focus-visible:outline-2"
      role="separator"
      aria-orientation="vertical"
      aria-label="调整日志面板宽度"
      :aria-valuenow="width"
      :aria-valuemin="MIN_WIDTH"
      :aria-valuemax="MAX_WIDTH"
      tabindex="0"
      @pointerdown="startDrag"
      @pointermove="drag"
      @keydown="resizeByKey"
    ></div>
    <div class="flex items-center gap-2 border-b border-[var(--line)] px-4 py-3">
      <span class="size-2 rounded-full" :class="logsPaused ? 'bg-warning' : 'bg-success'" aria-hidden="true"></span>
      <h2 class="grow text-sm font-semibold">实时日志{{ logsPaused ? '（已暂停）' : '' }}</h2>
      <button class="btn btn-ghost btn-sm btn-square" :aria-label="logsPaused ? '继续滚动' : '暂停滚动'" @click="togglePause">
        <PhPlay v-if="logsPaused" class="size-4" aria-hidden="true" />
        <PhPause v-else class="size-4" aria-hidden="true" />
      </button>
      <button class="btn btn-ghost btn-sm btn-square" aria-label="收起日志面板" @click="collapsed = true">
        <PhCaretDoubleRight class="size-4" aria-hidden="true" />
      </button>
    </div>
    <LogList layout="stacked" />
  </aside>
</template>
