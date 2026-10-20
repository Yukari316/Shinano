<script setup lang="ts">
import { computed, ref } from 'vue'
import { formatNumber } from '../format'

export interface BarPoint {
  label: string
  value: number
}

const props = defineProps<{ points: BarPoint[]; unit: string; summary: string }>()

const peak = computed(() => Math.max(1, ...props.points.map((point) => point.value)))
const peakPoint = computed(() => props.points.reduce<BarPoint | null>((best, point) => (best === null || point.value > best.value ? point : best), null))
const hovered = ref<BarPoint | null>(null)
const readout = computed(() => {
  const point = hovered.value ?? peakPoint.value
  if (!point) return ''
  if (!hovered.value && point.value === 0) return '暂无数据'
  return `${hovered.value ? '' : '峰值 '}${formatNumber(point.value)} ${props.unit}（${point.label}）`
})
const ticks = computed(() => {
  const points = props.points
  if (points.length === 0) return []
  return [points[0], points[Math.floor((points.length - 1) / 2)], points[points.length - 1]].map((point) => point?.label ?? '')
})
</script>

<template>
  <div class="flex flex-col gap-2">
    <div class="flex justify-end text-sm tabular-nums opacity-80" aria-hidden="true">{{ readout }}</div>
    <div class="flex h-36 items-end gap-0.5 border-b border-[var(--line)]" role="img" :aria-label="summary" @mouseleave="hovered = null">
      <div
        v-for="point in points"
        :key="point.label"
        class="min-w-1 flex-1 rounded-t bg-[var(--chart)] transition-opacity hover:opacity-80"
        :style="{ height: `${(point.value / peak) * 100}%` }"
        :title="`${point.label}：${formatNumber(point.value)} ${unit}`"
        @mouseenter="hovered = point"
      ></div>
    </div>
    <div class="flex justify-between text-xs opacity-70" aria-hidden="true">
      <span v-for="tick in ticks" :key="tick">{{ tick }}</span>
    </div>
  </div>
</template>
