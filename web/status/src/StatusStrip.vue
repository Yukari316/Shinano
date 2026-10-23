<script setup lang="ts">
import { computed, ref } from 'vue'
import { LEVELS, percent } from './labels'
import type { StatusBar } from './types'

const props = defineProps<{ bars: StatusBar[]; availability: number | null; format: Intl.DateTimeFormat; name: string }>()

const BAR_MINUTES = 30
const active = ref<StatusBar | null>(null)

// 悬停或键盘聚焦某一格时显示该时段的统计，否则显示 24 小时可用率。
const readout = computed(() => {
  const bar = active.value
  if (!bar) return `24 小时可用率 ${percent(props.availability)}`
  const start = new Date(bar.start)
  const end = new Date(start.getTime() + BAR_MINUTES * 60_000)
  const range = `${props.format.format(start)}–${props.format.format(end)}`
  if (bar.total === 0) return `${range} · 无探测记录`
  const level = bar.level ? LEVELS[bar.level].label : ''
  return `${range} · ${level} · 探测 ${bar.total} 次${bar.stopped > 0 ? `，停止 ${bar.stopped} 次` : ''}`
})

function label(bar: StatusBar): string {
  const start = props.format.format(new Date(bar.start))
  if (bar.total === 0) return `${start} 起 30 分钟：无探测记录`
  return `${start} 起 30 分钟：${bar.level ? LEVELS[bar.level].label : ''}，探测 ${bar.total} 次，停止 ${bar.stopped} 次`
}
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <div class="flex h-9 items-stretch gap-[2px]" role="group" :aria-label="`${name} 最近 24 小时状态，每格 30 分钟`" @mouseleave="active = null">
      <button
        v-for="bar in bars"
        :key="bar.start"
        type="button"
        class="min-w-0 flex-1 cursor-default rounded-[3px] transition-opacity hover:opacity-75 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-base-content"
        :class="bar.level ? LEVELS[bar.level].bar : 'bg-[var(--raise-strong)]'"
        :aria-label="label(bar)"
        @mouseenter="active = bar"
        @focus="active = bar"
        @blur="active = null"
      ></button>
    </div>
    <div class="flex items-center justify-between gap-3 text-xs">
      <span class="opacity-60">24 小时前</span>
      <span class="truncate tabular-nums opacity-80" aria-live="polite">{{ readout }}</span>
      <span class="opacity-60">现在</span>
    </div>
  </div>
</template>
