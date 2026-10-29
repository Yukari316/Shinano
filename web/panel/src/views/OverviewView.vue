<script setup lang="ts">
import { PhCheck, PhCube, PhWarning, PhX } from '@phosphor-icons/vue'
import { computed, onMounted, ref, watchEffect } from 'vue'
import { api, describeError } from '../api'
import BarChart from '../components/BarChart.vue'
import { formatBytes, formatNumber, HEALTH_BADGE, HEALTH_TEXT, recentDates, shortDate } from '../format'
import { pageSubtitle } from '../pageTitle'
import { plugins } from '../plugins'
import { restartStatus } from '../restart'
import { overview, overviewUpdatedAt, refreshOverview } from '../status'
import type { ArchiveGap, ConversationStat, DailyStat } from '../types'

const CHART_DAYS = 30
const GAP_WINDOW_DAYS = 7
const MS_PER_DAY = 86_400_000

const daily = ref<DailyStat[]>([])
const conversations = ref<ConversationStat[]>([])
const gaps = ref<ArchiveGap[]>([])
const error = ref('')
// 统计依赖机器人账号与数据库，取不到时只在对应卡片内说明，不影响服务状态的展示。
const statsNote = ref('')

onMounted(async () => {
  try {
    await refreshOverview()
  } catch (e) {
    error.value = describeError(e)
  }
  try {
    ;[daily.value, conversations.value, gaps.value] = await Promise.all([
      api.get<DailyStat[]>(`/api/stats/daily?days=${CHART_DAYS}`),
      api.get<ConversationStat[]>('/api/stats/conversations?days=1&scope=All'),
      api.get<ArchiveGap[]>('/api/archive-gaps')
    ])
  } catch (e) {
    statsNote.value = describeError(e)
  }
})

const byDate = computed(() => new Map(daily.value.map((day) => [day.date, day])))
const dates = computed(() => recentDates(CHART_DAYS))
const today = computed(() => byDate.value.get(dates.value[dates.value.length - 1] ?? ''))
const yesterday = computed(() => byDate.value.get(dates.value[dates.value.length - 2] ?? ''))
const change = computed(() => {
  const before = yesterday.value?.messages ?? 0
  if (before === 0) return null
  return Math.round((((today.value?.messages ?? 0) - before) / before) * 100)
})
const points = computed(() => dates.value.map((date) => ({ label: shortDate(date), value: byDate.value.get(date)?.messages ?? 0 })))
const activeGroups = computed(() => conversations.value.filter((item) => item.type === 'Group').length)
const activePrivate = computed(() => conversations.value.length - activeGroups.value)
const recentGaps = computed(() => gaps.value.filter((gap) => Date.now() - new Date(gap.endedAt).getTime() < GAP_WINDOW_DAYS * MS_PER_DAY).length)
const updatedAt = computed(() => overviewUpdatedAt.value?.toLocaleTimeString('zh-CN', { hour12: false }) ?? '—')
watchEffect(() => {
  pageSubtitle.value = `数据更新于 ${updatedAt.value} · 每 30 秒刷新`
})
const chartSummary = computed(() => {
  const total = points.value.reduce((sum, point) => sum + point.value, 0)
  return `近 ${CHART_DAYS} 天每日消息柱状图，共 ${total} 条`
})
</script>

<template>
  <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
  <div v-if="restartStatus?.lastResult?.outcome === 'RolledBack'" role="alert" class="alert alert-warning">
    <span>最近一次插件包变更未能就绪，已还原上一套插件包<template v-if="restartStatus.lastResult.reason">：{{ restartStatus.lastResult.reason }}</template></span>
    <RouterLink to="/plugins" class="btn btn-sm shrink-0">查看</RouterLink>
  </div>

  <section v-if="overview" class="surface card">
    <div class="card-body gap-4">
      <h2 class="card-title text-lg">
        服务状态
        <span class="badge" :class="HEALTH_BADGE[overview.overall]">{{ HEALTH_TEXT[overview.overall] }}</span>
      </h2>
      <div class="@container">
        <div class="grid grid-cols-2 gap-3 @lg:grid-cols-3 @3xl:grid-cols-6">
          <div v-for="component in overview.components" :key="component.component" class="tile flex flex-col gap-2 p-3">
            <span class="text-sm opacity-70">{{ component.name }}</span>
            <span class="badge badge-sm" :class="HEALTH_BADGE[component.level]">
              <PhCheck v-if="component.level === 'Normal'" class="size-3" aria-hidden="true" />
              <PhWarning v-else-if="component.level === 'Degraded'" class="size-3" aria-hidden="true" />
              <PhX v-else class="size-3" aria-hidden="true" />
              {{ HEALTH_TEXT[component.level] }}
            </span>
            <span v-if="component.detail" class="text-xs [overflow-wrap:anywhere]">{{ component.detail }}</span>
            <span class="text-xs opacity-60">自 {{ new Date(component.since).toLocaleTimeString('zh-CN', { hour12: false }) }}</span>
          </div>
        </div>
      </div>
    </div>
  </section>

  <section class="surface card">
    <div class="card-body gap-4">
      <div class="flex flex-wrap items-baseline justify-between gap-2">
        <h2 class="card-title text-lg">今日</h2>
        <span class="text-xs opacity-70">不含 Bot 发送的消息</span>
      </div>
      <p v-if="statsNote" class="text-sm opacity-70">{{ statsNote }}</p>
      <div v-if="!statsNote" class="grid grid-cols-[repeat(auto-fit,minmax(11rem,1fr))] gap-3">
        <div class="tile p-4">
          <div class="text-sm opacity-70">消息</div>
          <div class="text-3xl font-bold tabular-nums">{{ formatNumber(today?.messages ?? 0) }}</div>
          <div class="text-xs" :class="change !== null && change >= 0 ? 'text-success-strong' : 'opacity-70'">
            {{ change === null ? '昨日无数据' : `较昨日 ${change >= 0 ? '+' : ''}${change}%` }}
          </div>
        </div>
        <div class="tile p-4">
          <div class="text-sm opacity-70">字数</div>
          <div class="text-3xl font-bold tabular-nums">{{ formatNumber(today?.chars ?? 0) }}</div>
          <div class="text-xs opacity-70">
            {{ today && today.messages > 0 ? `平均每条 ${(today.chars / today.messages).toFixed(1)} 字` : '—' }}
          </div>
        </div>
        <div class="tile p-4">
          <div class="text-sm opacity-70">图片</div>
          <div class="text-3xl font-bold tabular-nums">{{ formatNumber(today?.images ?? 0) }}</div>
          <div class="text-xs opacity-70">语音 {{ today?.audios ?? 0 }} · 视频 {{ today?.videos ?? 0 }} · 文件 {{ today?.files ?? 0 }}</div>
        </div>
        <div class="tile p-4">
          <div class="text-sm opacity-70">活跃会话</div>
          <div class="text-3xl font-bold tabular-nums">{{ conversations.length }}</div>
          <div class="text-xs opacity-70">群 {{ activeGroups }} · 私聊 {{ activePrivate }}</div>
        </div>
      </div>
      <div v-if="!statsNote" class="flex flex-col gap-1">
        <span class="text-sm opacity-70">近 {{ CHART_DAYS }} 天每日消息</span>
        <BarChart :points="points" unit="条" :summary="chartSummary" />
      </div>
    </div>
  </section>

  <section class="surface card flex-1">
    <div class="card-body gap-4">
      <div class="flex items-center justify-between">
        <h2 class="card-title text-lg">插件与归档</h2>
        <RouterLink to="/plugins" class="link text-sm">管理插件</RouterLink>
      </div>
      <div class="grid grid-cols-[repeat(auto-fit,minmax(15rem,1fr))] gap-3">
        <RouterLink
          v-for="plugin in plugins"
          :key="plugin.id"
          :to="`/plugins/${plugin.id}`"
          class="tile flex items-center gap-3 p-3 hover:bg-[var(--raise-strong)]"
        >
          <PhCube class="size-7 shrink-0" :class="plugin.enabled ? 'text-[var(--chart)]' : 'opacity-50'" aria-hidden="true" />
          <span class="flex min-w-0 grow flex-col">
            <span class="truncate font-medium">{{ plugin.name || plugin.id }}</span>
            <span class="text-xs opacity-70">{{ plugin.error ?? (plugin.commandTypes.reduce((sum, type) => sum + type.commandCount, 0) + ' 条指令') }}</span>
          </span>
          <span class="badge badge-sm" :class="plugin.error ? 'badge-error' : plugin.enabled ? 'badge-success' : 'badge-ghost'">
            {{ plugin.error ? '失败' : plugin.enabled ? '启用' : '停用' }}
          </span>
        </RouterLink>
        <RouterLink to="/gaps" class="tile flex items-center gap-3 p-3 hover:bg-[var(--raise-strong)]">
          <PhWarning class="size-7 shrink-0" :class="recentGaps > 0 ? 'text-warning-strong' : 'opacity-50'" aria-hidden="true" />
          <span class="flex grow flex-col">
            <span class="font-medium">归档缺口</span>
            <span class="text-xs opacity-70">{{ statsNote ? '暂无法查询' : `近 ${GAP_WINDOW_DAYS} 天 ${recentGaps} 段` }}</span>
          </span>
          <span v-if="!statsNote" class="badge badge-sm" :class="recentGaps > 0 ? 'badge-warning' : 'badge-success'">{{ recentGaps > 0 ? '有缺口' : '无' }}</span>
        </RouterLink>
        <div v-if="overview" class="tile flex items-center gap-3 p-3">
          <span class="flex grow flex-col gap-1">
            <span class="font-medium">日志目录</span>
            <progress class="progress progress-primary" :value="overview.logs.usedBytes" :max="overview.logs.limitMiB * 1024 * 1024"></progress>
            <span class="text-xs opacity-70">{{ formatBytes(overview.logs.usedBytes) }} / {{ overview.logs.limitMiB }} MiB</span>
          </span>
        </div>
      </div>
    </div>
  </section>
</template>
