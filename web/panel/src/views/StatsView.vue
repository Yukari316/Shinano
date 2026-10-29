<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { api, describeError } from '../api'
import BarChart from '../components/BarChart.vue'
import SenderTable from '../components/SenderTable.vue'
import PageToolbar from '../components/PageToolbar.vue'
import StatFilters from '../components/StatFilters.vue'
import { formatNumber, recentDates, shortDate } from '../format'
import type { DailyStat, SenderStat } from '../types'

const days = ref(30)
const includeBot = ref(false)
const daily = ref<DailyStat[]>([])
const senders = ref<SenderStat[]>([])
const error = ref('')

const totals = computed(() =>
  daily.value.reduce(
    (sum, day) => ({
      messages: sum.messages + day.messages,
      chars: sum.chars + day.chars,
      images: sum.images + day.images
    }),
    { messages: 0, chars: 0, images: 0 }
  )
)
const newestFirst = computed(() => [...daily.value].reverse())
// 没有消息的日子接口不返回，图表按日期补 0。
const points = computed(() => {
  const byDate = new Map(daily.value.map((day) => [day.date, day.messages]))
  return recentDates(days.value).map((date) => ({ label: shortDate(date), value: byDate.get(date) ?? 0 }))
})
const chartSummary = computed(() => `近 ${days.value} 天每日消息柱状图，共 ${totals.value.messages} 条`)

watch(
  [days, includeBot],
  async () => {
    error.value = ''
    const query = `days=${days.value}&includeBot=${includeBot.value}`
    try {
      const [dailyRows, senderRows] = await Promise.all([
        api.get<DailyStat[]>(`/api/stats/daily?${query}`),
        api.get<SenderStat[]>(`/api/stats/senders?${query}&limit=20`)
      ])
      daily.value = dailyRows
      senders.value = senderRows
    } catch (e) {
      error.value = describeError(e)
    }
  },
  { immediate: true }
)
</script>

<template>
  <PageToolbar>
    <StatFilters v-model:days="days" v-model:include-bot="includeBot" />
  </PageToolbar>
  <div v-if="error" role="alert" class="alert alert-error">{{ error }}</div>
  <section class="surface card">
    <div class="card-body">
      <div class="stats stats-vertical lg:stats-horizontal">
        <div class="stat">
          <div class="stat-title">消息</div>
          <div class="stat-value text-2xl">{{ formatNumber(totals.messages) }}</div>
        </div>
        <div class="stat">
          <div class="stat-title">字数</div>
          <div class="stat-value text-2xl">{{ formatNumber(totals.chars) }}</div>
        </div>
        <div class="stat">
          <div class="stat-title">图片</div>
          <div class="stat-value text-2xl">{{ formatNumber(totals.images) }}</div>
        </div>
      </div>
    </div>
  </section>
  <section class="surface card">
    <div class="card-body">
      <h2 class="card-title">每日消息</h2>
      <BarChart :points="points" unit="条" :summary="chartSummary" />
      <details class="collapse-arrow collapse border border-[var(--line)]">
        <summary class="collapse-title text-sm">按日明细</summary>
        <div class="collapse-content overflow-x-auto">
          <table class="table table-sm">
            <thead>
              <tr>
                <th>日期</th>
                <th class="text-right">消息</th>
                <th class="text-right">字数</th>
                <th class="text-right">图片</th>
                <th class="text-right">文件</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="day in newestFirst" :key="day.date">
                <td>{{ day.date }}</td>
                <td class="text-right tabular-nums">{{ formatNumber(day.messages) }}</td>
                <td class="text-right tabular-nums">{{ formatNumber(day.chars) }}</td>
                <td class="text-right tabular-nums">{{ formatNumber(day.images) }}</td>
                <td class="text-right tabular-nums">{{ formatNumber(day.files) }}</td>
              </tr>
              <tr v-if="daily.length === 0">
                <td colspan="5" class="text-center opacity-60">暂无数据</td>
              </tr>
            </tbody>
          </table>
        </div>
      </details>
    </div>
  </section>
  <section class="surface card flex-1">
    <div class="card-body">
      <h2 class="card-title">发言排行</h2>
      <SenderTable :senders="senders" />
    </div>
  </section>
</template>
