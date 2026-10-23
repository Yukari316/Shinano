<script setup lang="ts">
import { PhCheckCircle, PhCircleDashed, PhMoon, PhSun, PhWarning, PhXCircle } from '@phosphor-icons/vue'
import { DARK_THEME, theme, toggleTheme } from '@shinano/theme'
import { computed, onBeforeUnmount, onMounted, ref, type Component } from 'vue'
import { describeFailure, LEVELS, OVERALL, percent, SERVICES } from './labels'
import StatusStrip from './StatusStrip.vue'
import { createFormatter } from './time'
import type { HealthLevel, StatusReport } from './types'

const REFRESH_MS = 30_000

const report = ref<StatusReport | null>(null)
const failed = ref(false)
let timer: number | undefined

const ICONS: Record<HealthLevel, Component> = { Normal: PhCheckCircle, Degraded: PhWarning, Stopped: PhXCircle }

const timeZone = computed(() => report.value?.timeZone ?? Intl.DateTimeFormat().resolvedOptions().timeZone)
const clock = computed(() => createFormatter(timeZone.value, { hour: '2-digit', minute: '2-digit', second: '2-digit' }))
const barTime = computed(() => createFormatter(timeZone.value, { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' }))

async function refresh(): Promise<void> {
  try {
    const response = await fetch('/api/status')
    if (!response.ok) throw new Error(String(response.status))
    report.value = (await response.json()) as StatusReport
    failed.value = false
  } catch {
    failed.value = true
  }
}

onMounted(() => {
  void refresh()
  timer = window.setInterval(() => void refresh(), REFRESH_MS)
})
onBeforeUnmount(() => window.clearInterval(timer))
</script>

<template>
  <div class="min-h-dvh bg-base-200">
    <main class="mx-auto flex max-w-4xl flex-col gap-4 px-4 py-6 sm:py-10">
      <header class="flex items-center justify-between gap-3">
        <h1 class="text-2xl font-bold">Shinano 服务状态</h1>
        <button class="btn btn-sm btn-ghost border-[var(--line)]" :aria-label="theme === DARK_THEME ? '切换到亮色主题' : '切换到暗色主题'" @click="toggleTheme">
          <PhSun v-if="theme === DARK_THEME" class="size-4" aria-hidden="true" />
          <PhMoon v-else class="size-4" aria-hidden="true" />
          {{ theme === DARK_THEME ? '亮色' : '暗色' }}
        </button>
      </header>

      <div v-if="failed" role="alert" class="alert alert-error">无法获取状态，监控进程可能已停止；页面会继续重试。</div>
      <div v-if="report && !report.configured" role="alert" class="alert alert-warning">
        监控进程没有找到部署配置文件，各项显示为未配置。请设置环境变量 SHINANO_DATA_DIR 或监控 appsettings.json 中的 Monitor:DeploymentConfigPath。
      </div>

      <section class="surface card" aria-live="polite">
        <div class="card-body flex-row items-center gap-4">
          <component
            :is="report?.overall ? ICONS[report.overall] : PhCircleDashed"
            class="size-12 shrink-0"
            :class="report?.overall ? LEVELS[report.overall].text : 'opacity-50'"
            weight="fill"
            aria-hidden="true"
          />
          <div class="flex min-w-0 flex-col gap-1">
            <h2 class="text-xl font-bold">{{ report?.overall ? OVERALL[report.overall] : '等待首次探测' }}</h2>
            <p class="text-sm opacity-70">
              <template v-if="report">更新于 {{ clock.format(new Date(report.generatedAt)) }} · 每 {{ report.intervalSeconds }} 秒探测一次</template>
              <template v-else>正在读取状态…</template>
            </p>
          </div>
        </div>
      </section>

      <section v-if="report" class="surface card">
        <ul class="divide-y divide-[var(--line)]">
          <li v-for="status in report.services" :key="status.service" class="flex flex-col gap-3 p-5 sm:p-6">
            <div class="flex flex-wrap items-start justify-between gap-x-4 gap-y-2">
              <div class="flex min-w-0 flex-col">
                <h3 class="font-semibold">{{ SERVICES[status.service].name }}</h3>
                <span class="text-xs opacity-60">{{ SERVICES[status.service].description }}</span>
              </div>
              <span v-if="status.level" class="badge gap-1" :class="LEVELS[status.level].badge">
                <component :is="ICONS[status.level]" class="size-3.5" weight="bold" aria-hidden="true" />
                {{ LEVELS[status.level].label }}
              </span>
              <span v-else class="badge badge-ghost">无数据</span>
            </div>
            <p v-if="describeFailure(status)" class="text-sm" :class="status.level ? LEVELS[status.level].text : ''">
              {{ describeFailure(status) }}
            </p>
            <StatusStrip :bars="status.bars" :availability="status.availability24h" :format="barTime" :name="SERVICES[status.service].name" />
            <p class="flex flex-wrap gap-x-4 gap-y-1 text-xs tabular-nums opacity-70">
              <span>最近检查 {{ status.checkedAt ? clock.format(new Date(status.checkedAt)) : '—' }}</span>
              <span>耗时 {{ status.latencyMs !== null ? `${status.latencyMs} ms` : '—' }}</span>
              <span>7 天可用率 {{ percent(status.availability7d) }}</span>
            </p>
          </li>
        </ul>
      </section>

      <footer class="text-center text-xs opacity-60">只读页面 · 探测记录保留 7 天 · 状态变化记录在监控日志中</footer>
    </main>
  </div>
</template>
