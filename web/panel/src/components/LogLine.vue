<script setup lang="ts">
import { computed } from 'vue'
import type { LogEntry } from '../types'

const props = defineProps<{ entry: LogEntry; layout: 'stacked' | 'row' }>()

const LEVELS: Record<string, { label: string; cls: string }> = {
  Verbose: { label: 'VRB', cls: 'opacity-60' },
  Debug: { label: 'DBG', cls: 'opacity-60' },
  Information: { label: 'INF', cls: 'text-info' },
  Warning: { label: 'WRN', cls: 'bg-warning text-warning-content' },
  Error: { label: 'ERR', cls: 'bg-error text-error-content' },
  Fatal: { label: 'FTL', cls: 'bg-error text-error-content' }
}

const level = computed(() => LEVELS[props.entry.level] ?? { label: props.entry.level, cls: '' })
const time = computed(() => new Date(props.entry.timestamp).toLocaleTimeString('zh-CN', { hour12: false }))
// 异常堆栈默认折叠，只显示首行（异常类型与消息）。
const exceptionHeadline = computed(() => props.entry.exception?.split('\n', 1)[0] ?? '')
</script>

<template>
  <div
    class="font-mono text-xs leading-5"
    :class="layout === 'row' ? 'grid grid-cols-[4.5rem_2.5rem_minmax(0,16rem)_minmax(0,1fr)] gap-x-3 px-3 hover:bg-[var(--raise)]' : 'flex flex-col'"
  >
    <div :class="layout === 'row' ? 'contents' : 'flex items-center gap-2'">
      <span class="opacity-60">{{ time }}</span>
      <span class="h-[1.125rem] rounded px-1 text-center text-[11px] font-medium leading-[1.125rem]" :class="level.cls">{{ level.label }}</span>
      <span class="truncate opacity-60" :title="entry.category">{{ entry.category }}</span>
    </div>
    <div class="min-w-0 [overflow-wrap:anywhere]">
      <span v-for="(segment, index) in entry.segments" :key="index" :class="segment.kind === 'Text' ? undefined : `log-${segment.kind}`">{{ segment.text }}</span>
      <details v-if="entry.exception" class="mt-0.5">
        <summary class="text-error-strong cursor-pointer select-none">{{ exceptionHeadline }}</summary>
        <pre class="text-error-strong whitespace-pre-wrap">{{ entry.exception }}</pre>
      </details>
    </div>
  </div>
</template>
