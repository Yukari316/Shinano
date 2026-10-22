<script setup lang="ts">
import { PhMoon, PhSun } from '@phosphor-icons/vue'
import { computed, watch } from 'vue'
import { useRoute } from 'vue-router'
import { appearance, backgroundUrl, refreshAppearance } from './appearance'
import LogPanel from './components/LogPanel.vue'
import RestartDialog from './components/RestartDialog.vue'
import RestartOverlay from './components/RestartOverlay.vue'
import SideNav from './components/SideNav.vue'
import { refreshPlugins } from './plugins'
import { pageTitle } from './pageTitle'
import { refreshRestart } from './restart'
import { startOverviewPolling, stopOverviewPolling } from './status'
import { DARK_THEME, theme, toggleTheme } from './theme'

const route = useRoute()
const isPublic = computed(() => route.meta.public === true)
// 日志页自己展示实时日志，此时不显示右侧日志面板，整个面板只保持一个连接。
const showLogPanel = computed(() => !isPublic.value && route.name !== 'logs')

const title = computed(() => pageTitle.value ?? route.meta.title)
const fill = computed(() => route.meta.fill === true)
watch(
  () => route.fullPath,
  () => {
    pageTitle.value = null
  }
)

const hasBackground = computed(() => backgroundUrl.value !== null)
const shellClass = computed(() => ({ 'has-bg': hasBackground.value, frosted: hasBackground.value && appearance.value.blur }))
const shellStyle = computed(() => ({
  '--panel-alpha': String(appearance.value.panelOpacity),
  '--card-alpha': String(appearance.value.cardOpacity),
  '--veil-alpha': String(appearance.value.veilOpacity),
  backgroundImage: backgroundUrl.value ? `url(${backgroundUrl.value})` : undefined
}))

// 登录后加载侧边栏、外观与总览状态；加载失败时保持默认值，不影响页面。
watch(
  isPublic,
  (publicPage) => {
    if (publicPage) {
      stopOverviewPolling()
      return
    }
    refreshPlugins().catch(() => undefined)
    refreshAppearance().catch(() => undefined)
    refreshRestart().catch(() => undefined)
    startOverviewPolling()
  },
  { immediate: true }
)
</script>

<template>
  <div v-if="isPublic" class="min-h-dvh bg-base-200">
    <RouterView />
  </div>
  <div v-else class="app-shell flex h-dvh gap-3 p-3" :class="shellClass" :style="shellStyle">
    <aside class="surface w-60 shrink-0 overflow-y-auto rounded-box">
      <SideNav />
    </aside>
    <!-- 标题、工具栏与底部操作栏固定，只有中间的页面内容滚动。 -->
    <main class="veil flex min-w-0 flex-1 flex-col overflow-hidden rounded-box">
      <header class="flex flex-wrap items-center justify-between gap-3 px-6 pb-4 pt-5">
        <h1 class="text-2xl font-bold [text-wrap:balance]">{{ title }}</h1>
        <button class="btn btn-sm btn-ghost border-[var(--line)]" :aria-label="theme === DARK_THEME ? '切换到亮色主题' : '切换到暗色主题'" @click="toggleTheme">
          <PhSun v-if="theme === DARK_THEME" class="size-4" aria-hidden="true" />
          <PhMoon v-else class="size-4" aria-hidden="true" />
          {{ theme === DARK_THEME ? '亮色' : '暗色' }}
        </button>
      </header>
      <div id="page-toolbar" class="px-6"></div>
      <div class="min-h-0 flex-1 px-6 pb-6" :class="fill ? 'flex flex-col' : 'overflow-y-auto'">
        <div class="flex flex-col gap-4" :class="{ 'min-h-0 flex-1': fill }">
          <RouterView :key="route.fullPath" />
        </div>
      </div>
      <div id="page-footer" class="px-6"></div>
    </main>
    <LogPanel v-if="showLogPanel" />
    <RestartDialog />
    <RestartOverlay />
  </div>
</template>
