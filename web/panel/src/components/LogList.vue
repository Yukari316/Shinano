<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { errorCount, visibleLogs, warningCount } from '../logStream'
import LogLine from './LogLine.vue'

const props = defineProps<{ layout: 'stacked' | 'row' }>()

type Filter = 'all' | 'warning' | 'error'
const filter = ref<Filter>('all')
const container = ref<HTMLElement | null>(null)
// 用户向上翻看时不自动滚动，回到底部后恢复。
const following = ref(true)

const entries = computed(() =>
  visibleLogs.value.filter((entry) => {
    if (filter.value === 'warning') return entry.level === 'Warning'
    if (filter.value === 'error') return entry.level === 'Error' || entry.level === 'Fatal'
    return true
  })
)

const NEAR_BOTTOM_PX = 24

function onScroll(): void {
  const element = container.value
  if (!element) return
  following.value = element.scrollHeight - element.scrollTop - element.clientHeight < NEAR_BOTTOM_PX
}

// 监听最后一条而不是条数：缓冲满 1000 条后条数不再变化，新日志仍要跟随到底部。
watch(
  () => entries.value.at(-1),
  async () => {
    if (!following.value) return
    await nextTick()
    container.value?.scrollTo({ top: container.value.scrollHeight })
  },
  { immediate: true }
)
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <div class="flex flex-wrap items-center gap-2 px-4 py-2">
      <div class="join" role="group" aria-label="按等级筛选">
        <button class="btn join-item btn-xs" :class="{ 'btn-active': filter === 'all' }" :aria-pressed="filter === 'all'" @click="filter = 'all'">全部</button>
        <button class="btn join-item btn-xs" :class="{ 'btn-active': filter === 'warning' }" :aria-pressed="filter === 'warning'" @click="filter = 'warning'">
          警告 <span class="text-warning-strong">{{ warningCount }}</span>
        </button>
        <button class="btn join-item btn-xs" :class="{ 'btn-active': filter === 'error' }" :aria-pressed="filter === 'error'" @click="filter = 'error'">
          错误 <span class="text-error-strong">{{ errorCount }}</span>
        </button>
      </div>
      <span class="grow"></span>
      <span class="font-mono text-xs" aria-label="参数颜色说明">
        <span class="log-String">string</span>
        <span class="log-Number ml-2">num</span>
        <span class="log-Boolean ml-2">bool</span>
      </span>
    </div>
    <div
      ref="container"
      class="min-h-0 flex-1 overflow-y-auto pb-3"
      :class="props.layout === 'stacked' ? 'flex flex-col gap-2 px-4' : ''"
      role="log"
      aria-live="off"
      @scroll="onScroll"
    >
      <LogLine v-for="entry in entries" :key="entry.sequence" :entry="entry" :layout="props.layout" />
      <p v-if="entries.length === 0" class="px-4 py-6 text-center text-sm opacity-60">暂无日志</p>
    </div>
  </div>
</template>
