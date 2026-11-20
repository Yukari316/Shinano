<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { api, describeError } from '../api'
import { formatTime, isoDate, recentDates, shortDate } from '../format'
import { overview } from '../status'
import type { ArchiveGap } from '../types'

const STRIP_DAYS = 30

const CAUSE_TEXT: Record<ArchiveGap['cause'], string> = {
  DatabaseOffline: '数据库离线',
  BotDisconnected: 'LLBot 断线',
  Backlog: '归档积压'
}

const gaps = ref<ArchiveGap[]>([])
const error = ref('')
const active = ref<string | null>(null)

onMounted(async () => {
  try {
    gaps.value = await api.get<ArchiveGap[]>('/api/archive-gaps')
  } catch (e) {
    error.value = describeError(e)
  }
})

interface GapDay {
  date: string
  causes: ArchiveGap['cause'][]
}

// 每天一格（按部署时区分日，与统计一致）：与缺口区间有重叠的日子记下原因；数据库离线最严重，用 error 色，其余缺口用 warning 色。
const days = computed<GapDay[]>(() => {
  const timeZone = overview.value?.timeZone
  const spans = gaps.value.map((gap) => ({
    cause: gap.cause,
    from: isoDate(new Date(gap.startedAt), timeZone),
    to: isoDate(new Date(gap.endedAt), timeZone)
  }))
  return recentDates(STRIP_DAYS, timeZone).map((date) => {
    const causes = spans.filter((span) => span.from <= date && date <= span.to).map((span) => span.cause)
    return { date, causes: [...new Set(causes)] }
  })
})
const gapDays = computed(() => days.value.filter((day) => day.causes.length > 0).length)

function cellClass(day: GapDay): string {
  if (day.causes.includes('DatabaseOffline')) return 'bg-error'
  return day.causes.length > 0 ? 'bg-warning' : 'bg-success'
}

function describe(day: GapDay): string {
  return day.causes.length === 0 ? `${day.date}：没有缺口` : `${day.date}：${day.causes.map((cause) => CAUSE_TEXT[cause]).join('、')}`
}

const readout = computed(() => {
  const day = days.value.find((item) => item.date === active.value)
  return day ? describe(day) : `近 ${STRIP_DAYS} 天有 ${gapDays.value} 天存在缺口`
})
const today = computed(() => isoDate(new Date(), overview.value?.timeZone))
</script>

<template>
  <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>

  <section class="surface card">
    <div class="card-body gap-3">
      <h2 class="card-title">近 {{ STRIP_DAYS }} 天</h2>
      <div class="flex h-9 items-stretch gap-[3px]" role="group" :aria-label="`近 ${STRIP_DAYS} 天的归档缺口，每格一天`" @mouseleave="active = null">
        <button
          v-for="day in days"
          :key="day.date"
          type="button"
          class="min-w-0 flex-1 cursor-default rounded-[3px] transition-opacity hover:opacity-75 focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-base-content"
          :class="cellClass(day)"
          :aria-label="describe(day)"
          @mouseenter="active = day.date"
          @focus="active = day.date"
          @blur="active = null"
        ></button>
      </div>
      <div class="flex items-center justify-between gap-3 text-xs">
        <span class="opacity-60">{{ shortDate(days[0]?.date ?? today) }}</span>
        <span class="truncate tabular-nums opacity-80" aria-live="polite">{{ readout }}</span>
        <span class="opacity-60">今天</span>
      </div>
      <div class="flex flex-wrap gap-4 text-xs opacity-80">
        <span class="flex items-center gap-1.5"><span class="size-3 rounded-[3px] bg-success" aria-hidden="true"></span>没有缺口</span>
        <span class="flex items-center gap-1.5"><span class="size-3 rounded-[3px] bg-warning" aria-hidden="true"></span>LLBot 断线或归档积压</span>
        <span class="flex items-center gap-1.5"><span class="size-3 rounded-[3px] bg-error" aria-hidden="true"></span>数据库离线</span>
      </div>
    </div>
  </section>

  <section class="surface card flex-1">
    <div class="card-body">
      <h2 class="card-title">缺口记录</h2>
      <p class="text-sm opacity-70">以下时间段内的消息未能完整归档，且无法补回。</p>
      <table class="table">
        <thead>
          <tr>
            <th>原因</th>
            <th>开始</th>
            <th>结束</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="gap in gaps" :key="`${gap.cause}-${gap.startedAt}`">
            <td><span class="badge" :class="gap.cause === 'DatabaseOffline' ? 'badge-error' : 'badge-warning'">{{ CAUSE_TEXT[gap.cause] }}</span></td>
            <td>{{ formatTime(gap.startedAt) }}</td>
            <td>{{ formatTime(gap.endedAt) }}</td>
          </tr>
          <tr v-if="gaps.length === 0">
            <td colspan="3" class="text-center opacity-60">没有归档缺口</td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
